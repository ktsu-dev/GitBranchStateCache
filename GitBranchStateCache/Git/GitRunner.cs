// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Git;

using System.Diagnostics;
using System.Text;
using ktsu.GitBranchStateCache.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs git as a child process.
/// </summary>
/// <remarks>
/// Two things here are load bearing and neither is obvious from the outside.
/// <para>
/// <strong>The credential never reaches a command line.</strong> It is handed to git as configuration
/// through the environment, as <c>GIT_CONFIG_KEY_n</c> and <c>GIT_CONFIG_VALUE_n</c>. On Linux a
/// process's command line is world readable through <c>/proc/pid/cmdline</c> while its environment is
/// readable only by its owner, and this service handles many different people's forge credentials, so
/// the distinction is the whole point. That also rules out <c>-c http.extraHeader=</c> and a URL
/// carrying userinfo, both of which put it on the command line.
/// </para>
/// <para>
/// <strong>Nothing on the host can influence a run.</strong> System and global configuration are
/// switched off, and any inherited <c>GIT_*</c> variable is dropped, so a credential helper, a proxy,
/// or an alias configured for whoever the process runs as cannot change what git does here. A
/// credential helper in particular would be able to answer for a credential this service was never
/// given, which would turn a refused request into a served one.
/// </para>
/// </remarks>
/// <param name="options">The configured options.</param>
public sealed class GitRunner(IOptions<GitBranchStateCacheOptions> options) : IGitRunner
{
	/// <summary>
	/// How long the output streams are drained for after a kill before they are given up on.
	/// </summary>
	/// <remarks>
	/// A killed process closes its pipes, so this normally completes immediately. It is bounded
	/// because a grandchild that inherited the pipe and outlived the kill would otherwise hold the
	/// read open, and this path already runs on a request that is being abandoned.
	/// </remarks>
	private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Decodes git output strictly, so an undecodable path fails loudly instead of being replaced.
	/// </summary>
	/// <remarks>
	/// The replacement character would turn a path this service cannot represent into a path that
	/// looks fine and matches nothing, which is exactly the silent failure to warn about a stale asset
	/// that this service exists to prevent.
	/// </remarks>
	private static readonly Encoding StrictUtf8 = new UTF8Encoding(
		encoderShouldEmitUTF8Identifier: false,
		throwOnInvalidBytes: true);

	/// <inheritdoc />
	public async Task<GitResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
	{
		Ensure.NotNull(invocation);

		using Process process = new() { StartInfo = BuildStartInfo(invocation) };
		process.Start();

		// git is never fed anything, and a child holding an open stdin it is waiting on is a hang
		// rather than an error.
		process.StandardInput.Close();

		Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

		using CancellationTokenSource timeout = new(invocation.Timeout);
		using CancellationTokenSource linked =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

		try
		{
			await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			Kill(process);
			await DrainAsync(standardOutput, standardError).ConfigureAwait(false);

			// A timeout is this service's own decision and has an answer to report. A cancellation is
			// the caller giving up, and there is nobody left to report anything to.
			cancellationToken.ThrowIfCancellationRequested();

			return new GitResult(-1, string.Empty, "The git command exceeded its timeout.", TimedOut: true);
		}

		try
		{
			return new GitResult(
				process.ExitCode,
				await standardOutput.ConfigureAwait(false),
				await standardError.ConfigureAwait(false),
				TimedOut: false);
		}
		catch (DecoderFallbackException)
		{
			return new GitResult(
				-1,
				string.Empty,
				"git produced output that is not valid UTF-8, so it cannot be read without guessing.",
				TimedOut: false);
		}
	}

	/// <summary>
	/// Kills the process and everything it started.
	/// </summary>
	/// <remarks>
	/// The tree, not just the process: git delegates transport to a helper child, and killing only the
	/// parent leaves that helper holding a connection and a pipe. Leaking those is the most likely
	/// operational failure of a service shaped like this.
	/// </remarks>
	private static void Kill(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException)
		{
			// The process exited between the check and the kill. Nothing left to do.
		}
		catch (NotSupportedException)
		{
			// Killing a tree is unsupported on this platform, and the process is already gone or will
			// be reaped when its handle is disposed.
		}
	}

	private static async Task DrainAsync(Task<string> standardOutput, Task<string> standardError)
	{
		try
		{
			await Task.WhenAll(standardOutput, standardError).WaitAsync(DrainTimeout).ConfigureAwait(false);
		}
		catch (Exception failure) when (failure is TimeoutException or DecoderFallbackException)
		{
			// The output of a killed command is not reported, so failing to read it changes nothing.
		}
	}

	private ProcessStartInfo BuildStartInfo(GitInvocation invocation)
	{
		GitBranchStateCacheOptions settings = options.Value;

		ProcessStartInfo startInfo = new()
		{
			FileName = settings.GitExecutable,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = StrictUtf8,
			StandardErrorEncoding = StrictUtf8,
		};

		if (invocation.WorkingDirectory is not null)
		{
			startInfo.WorkingDirectory = invocation.WorkingDirectory;
		}

		foreach (string argument in invocation.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		ApplyEnvironment(startInfo, invocation, settings);
		return startInfo;
	}

	/// <summary>
	/// Applies the environment every run gets, including the caller's credential.
	/// </summary>
	/// <remarks>
	/// Internal so the tests can assert on the environment directly. What it puts where is the whole
	/// of this class's security posture, and asserting it through the behaviour of a child process
	/// would only ever cover the parts a child happens to report.
	/// </remarks>
	/// <param name="startInfo">The process being prepared.</param>
	/// <param name="invocation">What is being run.</param>
	/// <param name="settings">The configured options.</param>
	internal static void ApplyEnvironment(
		ProcessStartInfo startInfo,
		GitInvocation invocation,
		GitBranchStateCacheOptions settings)
	{
		foreach (string inherited in startInfo.Environment.Keys
			.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
			.ToArray())
		{
			startInfo.Environment.Remove(inherited);
		}

		startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
		startInfo.Environment["GIT_CONFIG_GLOBAL"] = GlobalConfigPath(settings);

		// No prompting, ever. Without this a missing or refused credential turns a request into a
		// process waiting on a terminal that is not there, which presents as a hang rather than a 401.
		startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
		startInfo.Environment["GCM_INTERACTIVE"] = "never";

		// The mirrors are blobless, and nothing this service runs reads file content. If some future
		// operation does, this turns it into a visible error during testing rather than an enormous
		// unplanned fetch in production.
		startInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";

		ApplyConfigEnvironment(startInfo, invocation);
	}

	/// <summary>
	/// Hands git its per-run configuration, including the caller's credential, through the environment.
	/// </summary>
	private static void ApplyConfigEnvironment(ProcessStartInfo startInfo, GitInvocation invocation)
	{
		List<KeyValuePair<string, string>> entries =
		[
			// An empty value resets the helper list, so no credential manager configured for the
			// account this process runs as can answer on a caller's behalf.
			new("credential.helper", string.Empty),
		];

		if (invocation.Authorization is { Length: > 0 } authorization && invocation.CredentialScope is not null)
		{
			// Scoped to the upstream rather than set for all of http, because git matches this
			// configuration by URL prefix and a redirect leading off the forge would otherwise carry
			// the caller's credential with it.
			entries.Add(new(
				$"http.{invocation.CredentialScope.AbsoluteUri}.extraHeader",
				$"Authorization: {authorization}"));
		}

		startInfo.Environment["GIT_CONFIG_COUNT"] = entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

		for (int index = 0; index < entries.Count; index++)
		{
			startInfo.Environment[$"GIT_CONFIG_KEY_{index}"] = entries[index].Key;
			startInfo.Environment[$"GIT_CONFIG_VALUE_{index}"] = entries[index].Value;
		}
	}

	/// <summary>
	/// Gets the file git is told to treat as its global configuration.
	/// </summary>
	/// <remarks>
	/// A path inside the mirror root rather than the account's real one, so whatever is configured for
	/// the user this service runs as cannot reach a run. The startup check creates it empty; git
	/// tolerates it being absent, so a missing file is a safe state rather than a failure.
	/// </remarks>
	internal static string GlobalConfigPath(GitBranchStateCacheOptions settings) =>
		Path.Combine(settings.MirrorRoot, "gitconfig");
}
