// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Git;

using System.Text;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.RunCommand;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;
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
/// <strong>Nothing configured for the account this runs as can influence a run.</strong> System and
/// global configuration are switched off, and any inherited <c>GIT_*</c> variable is dropped, so a
/// credential helper, a proxy, or an alias configured for whoever the process runs as cannot change
/// what git does here. A credential helper in particular would be able to answer for a credential
/// this service was never given, which would turn a refused request into a served one.
/// </para>
/// <para>
/// Repository-local configuration is the one layer left in play, deliberately: a mirror's own config
/// is what carries its fetch refspec. That config is only ever what this service wrote when it
/// created the mirror.
/// </para>
/// </remarks>
/// <param name="options">The configured options.</param>
public sealed class GitRunner(IOptions<GitBranchStateCacheOptions> options) : IGitRunner
{
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

	/// <summary>
	/// Decodes the output of a run whose standard output is discarded, where only standard error is
	/// kept and it is only ever shown or searched for git's own ASCII messages.
	/// </summary>
	private static readonly Encoding LenientUtf8 = new UTF8Encoding(
		encoderShouldEmitUTF8Identifier: false,
		throwOnInvalidBytes: false);

	/// <inheritdoc />
	/// <remarks>
	/// Starting, reading and killing the process is <see cref="RunCommand.ExecuteAsync(string, IEnumerable{string}, OutputHandler, CommandOptions, CancellationToken)"/>'s
	/// job: it kills the whole tree on cancellation, because git delegates transport to a helper child
	/// that would otherwise be left holding a connection and a pipe, and it stops reading once the
	/// process is gone rather than waiting on a pipe a surviving grandchild may hold open. What stays
	/// here is what is particular to git: the environment, and telling this service's own timeout
	/// apart from the caller giving up.
	/// </remarks>
	public async Task<GitResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
	{
		Ensure.NotNull(invocation);

		GitBranchStateCacheOptions settings = options.Value;

		StringBuilder standardOutput = new();
		StringBuilder standardError = new();
		OutputHandler output = invocation.DiscardStandardOutput
			? new(
				_ => { },
				chunk => standardError.Append(chunk),
				LenientUtf8)
			: new(
				chunk => standardOutput.Append(chunk),
				chunk => standardError.Append(chunk),
				StrictUtf8);

		CommandOptions commandOptions = new()
		{
			// Resolved here because the option only takes an absolute path, and a relative one has always
			// meant relative to this process's current directory.
			WorkingDirectory = invocation.WorkingDirectory is null
				? null
				: Path.GetFullPath(invocation.WorkingDirectory).As<AbsoluteDirectoryPath>(),
			EnvironmentVariables = BuildEnvironment(invocation, settings, Environment.GetEnvironmentVariables()),

			// git is never fed anything, and a child holding an open stdin it is waiting on is a hang
			// rather than an error.
			StandardInput = StandardInputMode.Closed,
		};

		using CancellationTokenSource timeout = new(invocation.Timeout);
		using CancellationTokenSource linked =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

		try
		{
			int exitCode = await RunCommand.ExecuteAsync(
				settings.GitExecutable,
				invocation.Arguments,
				output,
				commandOptions,
				linked.Token).ConfigureAwait(false);

			return new GitResult(exitCode, standardOutput.ToString(), standardError.ToString(), TimedOut: false);
		}
		catch (OperationCanceledException)
		{
			// A timeout is this service's own decision and has an answer to report. A cancellation is
			// the caller giving up, and there is nobody left to report anything to.
			cancellationToken.ThrowIfCancellationRequested();

			return new GitResult(-1, string.Empty, "The git command exceeded its timeout.", TimedOut: true);
		}
		catch (Exception failure) when (IsDecodeFailure(failure))
		{
			return new GitResult(
				-1,
				string.Empty,
				"git produced output that is not valid UTF-8, so it cannot be read without guessing.",
				TimedOut: false);
		}
	}

	/// <summary>
	/// Whether a failure is the strict encoding refusing git's output.
	/// </summary>
	/// <remarks>
	/// The output is read on background tasks, so the decoder's exception can arrive wrapped.
	/// </remarks>
	private static bool IsDecodeFailure(Exception failure) => failure switch
	{
		DecoderFallbackException => true,
		AggregateException aggregate => aggregate.Flatten().InnerExceptions.Any(IsDecodeFailure),
		_ => failure.InnerException is not null && IsDecodeFailure(failure.InnerException),
	};

	/// <summary>
	/// Builds the environment every run gets, including the caller's credential, as an overlay on the
	/// environment the child would otherwise inherit.
	/// </summary>
	/// <remarks>
	/// Internal so the tests can assert on the environment directly. What it puts where is the whole
	/// of this class's security posture, and asserting it through the behaviour of a child process
	/// would only ever cover the parts a child happens to report.
	/// </remarks>
	/// <param name="invocation">What is being run.</param>
	/// <param name="settings">The configured options.</param>
	/// <param name="inherited">The environment the child would otherwise inherit.</param>
	/// <returns>The variables to set, with a null value for each inherited variable to remove.</returns>
	internal static Dictionary<string, string?> BuildEnvironment(
		GitInvocation invocation,
		GitBranchStateCacheOptions settings,
		System.Collections.IDictionary inherited)
	{
		Ensure.NotNull(inherited);

		Dictionary<string, string?> environment = new(StringComparer.OrdinalIgnoreCase);

		foreach (string name in inherited.Keys.OfType<string>()
			.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)))
		{
			environment[name] = null;
		}

		environment["GIT_CONFIG_NOSYSTEM"] = "1";
		environment["GIT_CONFIG_GLOBAL"] = GlobalConfigPath(settings);

		// No prompting, ever. Without this a missing or refused credential turns a request into a
		// process waiting on a terminal that is not there, which presents as a hang rather than a 401.
		environment["GIT_TERMINAL_PROMPT"] = "0";
		environment["GCM_INTERACTIVE"] = "never";

		// The mirrors are blobless, and nothing this service runs reads file content. If some future
		// operation does, this turns it into a visible error during testing rather than an enormous
		// unplanned fetch in production.
		environment["GIT_NO_LAZY_FETCH"] = "1";

		ApplyConfigEnvironment(environment, invocation);
		return environment;
	}

	/// <summary>
	/// Hands git its per-run configuration, including the caller's credential, through the environment.
	/// </summary>
	private static void ApplyConfigEnvironment(Dictionary<string, string?> environment, GitInvocation invocation)
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

		environment["GIT_CONFIG_COUNT"] = entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

		for (int index = 0; index < entries.Count; index++)
		{
			environment[$"GIT_CONFIG_KEY_{index}"] = entries[index].Key;
			environment[$"GIT_CONFIG_VALUE_{index}"] = entries[index].Value;
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
