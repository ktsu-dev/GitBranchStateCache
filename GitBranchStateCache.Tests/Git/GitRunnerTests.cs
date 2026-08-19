// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Git;

using System.Diagnostics;
using System.Runtime.InteropServices;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Exercises the one unit that starts a process, against real processes.
/// </summary>
/// <remarks>
/// Process management is the least .NET-shaped part of this design and the most likely source of
/// leaks under load, so it is the one place where a fake would be testing the wrong thing.
/// </remarks>
[TestClass]
public class GitRunnerTests
{
	private static readonly bool OnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	/// <summary>The process that outlives its parent, used to prove the whole tree is reaped.</summary>
	private static string SleeperChildName => OnWindows ? "PING" : "sleep";

	private static string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "gitbranchstatecache-tests");

	private static GitRunner Build(string executable = "git")
	{
		Directory.CreateDirectory(TempRoot);

		return new GitRunner(Options.Create(new GitBranchStateCacheOptions
		{
			MirrorRoot = TempRoot,
			GitExecutable = executable,
		}));
	}

	/// <summary>Builds an invocation for a process that runs for a long time on this platform.</summary>
	private static (string Executable, string[] Arguments) Sleeper() => OnWindows
		? ("cmd.exe", ["/c", "ping -n 60 127.0.0.1 > nul"])
		: ("/bin/sh", ["-c", "sleep 60"]);

	private static int[] ProcessIds(string name)
	{
		try
		{
			return [.. Process.GetProcessesByName(name).Select(process => process.Id)];
		}
		catch (InvalidOperationException)
		{
			return [];
		}
	}

	[TestMethod]
	public async Task RunAsync_Version_Succeeds()
	{
		GitResult result = await Build().RunAsync(
			new GitInvocation { Arguments = ["--version"], Timeout = TimeSpan.FromSeconds(30) },
			CancellationToken.None);

		Assert.IsTrue(result.Succeeded, result.StandardError);
		Assert.StartsWith("git version", result.StandardOutput.Trim());
	}

	[TestMethod]
	public async Task RunAsync_AFailingCommand_ReportsItRatherThanThrowing()
	{
		// A non-zero exit is an answer, not an exception. Most of this service's decisions are made
		// from one.
		GitResult result = await Build().RunAsync(
			new GitInvocation { Arguments = ["cat-file", "-e", "nonsense"], Timeout = TimeSpan.FromSeconds(30) },
			CancellationToken.None);

		Assert.IsFalse(result.Succeeded);
		Assert.AreNotEqual(0, result.ExitCode);
	}

	[TestMethod]
	public async Task RunAsync_WithACredential_HandsItToGitThroughTheEnvironmentAndNotTheCommandLine()
	{
		// The arguments are literally "config --list" and carry nothing else, so git knowing about the
		// header at all is proof it arrived through the environment. That is the difference that
		// matters: on Linux a command line is world readable and an environment block is not, and this
		// service handles many different people's forge credentials.
		const string credential = "Basic dXNlcjp0b2tlbg==";

		GitResult result = await Build().RunAsync(
			new GitInvocation
			{
				WorkingDirectory = OutsideAnyRepository(),
				Arguments = ["config", "--list"],
				CredentialScope = new Uri("https://github.com"),
				Authorization = credential,
				Timeout = TimeSpan.FromSeconds(30),
			},
			CancellationToken.None);

		Assert.IsTrue(result.Succeeded, result.StandardError);
		Assert.Contains(credential, result.StandardOutput);
		Assert.Contains("extraheader", result.StandardOutput);
	}

	[TestMethod]
	public async Task RunAsync_WithoutACredential_SendsNoHeaderAndDisablesCredentialHelpers()
	{
		// A helper configured for whoever this process runs as would be able to answer for a
		// credential this service was never given, turning a refused request into a served one.
		GitResult result = await Build().RunAsync(
			new GitInvocation
			{
				WorkingDirectory = OutsideAnyRepository(),
				Arguments = ["config", "--list"],
				Timeout = TimeSpan.FromSeconds(30),
			},
			CancellationToken.None);

		Assert.DoesNotContain("extraheader", result.StandardOutput);
		Assert.Contains("credential.helper=", result.StandardOutput);
	}

	/// <summary>
	/// A directory that is not inside any git repository.
	/// </summary>
	/// <remarks>
	/// The two tests above ask git what configuration it can see, so they have to run where the only
	/// answer is the configuration this service supplied. Without a working directory the child
	/// inherits the test process's, which under CI is the checked-out repository, and
	/// <c>actions/checkout</c> writes its own <c>http.https://github.com/.extraheader</c> into that
	/// repository's local config to carry the workflow token. The negative assertion then fails on a
	/// header this service never sent.
	/// <para>
	/// Worth knowing beyond the test: repository-local configuration is the one layer a run is not
	/// insulated from, and deliberately so, because a mirror's own config is what carries its fetch
	/// refspec. It is only ever configuration this service wrote.
	/// </para>
	/// </remarks>
	private static string OutsideAnyRepository()
	{
		Directory.CreateDirectory(TempRoot);
		return TempRoot;
	}

	[TestMethod]
	public void ApplyEnvironment_SetsTheFlagsThatKeepARunPredictable()
	{
		ProcessStartInfo startInfo = new();
		startInfo.Environment["GIT_DIR"] = "/somewhere/inherited";

		GitRunner.ApplyEnvironment(
			startInfo,
			new GitInvocation { Arguments = ["--version"], Timeout = TimeSpan.FromSeconds(1) },
			new GitBranchStateCacheOptions { MirrorRoot = TempRoot });

		// GIT_NO_LAZY_FETCH turns a demand for filtered content into a visible error rather than an
		// enormous unplanned fetch, and no terminal prompt turns a missing credential into a refusal
		// rather than a process waiting on a terminal that is not there.
		Assert.AreEqual("1", startInfo.Environment["GIT_NO_LAZY_FETCH"]);
		Assert.AreEqual("0", startInfo.Environment["GIT_TERMINAL_PROMPT"]);
		Assert.AreEqual("1", startInfo.Environment["GIT_CONFIG_NOSYSTEM"]);

		// An inherited GIT_DIR would point every run at a repository nobody asked for.
		Assert.IsFalse(startInfo.Environment.ContainsKey("GIT_DIR"));
	}

	[TestMethod]
	public void ApplyEnvironment_WithACredential_ScopesItToTheUpstream()
	{
		const string credential = "Basic dXNlcjp0b2tlbg==";
		ProcessStartInfo startInfo = new();

		GitRunner.ApplyEnvironment(
			startInfo,
			new GitInvocation
			{
				Arguments = ["ls-remote", "https://github.com/studio/game.git"],
				CredentialScope = new Uri("https://github.com"),
				Authorization = credential,
				Timeout = TimeSpan.FromSeconds(1),
			},
			new GitBranchStateCacheOptions { MirrorRoot = TempRoot });

		// Scoped to the upstream rather than set for all of http, because git matches this
		// configuration by URL prefix and a redirect leading off the forge would otherwise carry the
		// caller's credential with it.
		Assert.AreEqual("2", startInfo.Environment["GIT_CONFIG_COUNT"]);
		Assert.AreEqual("http.https://github.com/.extraHeader", startInfo.Environment["GIT_CONFIG_KEY_1"]);
		Assert.AreEqual($"Authorization: {credential}", startInfo.Environment["GIT_CONFIG_VALUE_1"]);
	}

	[TestMethod]
	public void ApplyEnvironment_WithoutACredential_SetsNoHeader()
	{
		ProcessStartInfo startInfo = new();

		GitRunner.ApplyEnvironment(
			startInfo,
			new GitInvocation { Arguments = ["--version"], Timeout = TimeSpan.FromSeconds(1) },
			new GitBranchStateCacheOptions { MirrorRoot = TempRoot });

		Assert.AreEqual("1", startInfo.Environment["GIT_CONFIG_COUNT"]);
		Assert.AreEqual("credential.helper", startInfo.Environment["GIT_CONFIG_KEY_0"]);
	}

	[TestMethod]
	public async Task RunAsync_ExceedingItsTimeout_ReportsTimedOutAndKillsTheTree()
	{
		(string executable, string[] arguments) = Sleeper();
		int[] before = ProcessIds(SleeperChildName);

		Task<GitResult> running = Build(executable).RunAsync(
			new GitInvocation { Arguments = arguments, Timeout = TimeSpan.FromSeconds(2) },
			CancellationToken.None);

		int[] started = await WaitForNewChildAsync(before);
		GitResult result = await running;

		Assert.IsTrue(result.TimedOut);
		Assert.IsFalse(result.Succeeded);
		await AssertAllExitedAsync(started);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheRequestIsAbandoned_KillsTheTreeAndPropagatesTheCancellation()
	{
		// An editor that gives up mid-poll must not leave a git process behind. Thirty seconds later
		// it asks again, so a leak here compounds rather than clears.
		(string executable, string[] arguments) = Sleeper();
		int[] before = ProcessIds(SleeperChildName);

		using CancellationTokenSource cancellation = new();

		Task<GitResult> running = Build(executable).RunAsync(
			new GitInvocation { Arguments = arguments, Timeout = TimeSpan.FromMinutes(5) },
			cancellation.Token);

		int[] started = await WaitForNewChildAsync(before);
		await cancellation.CancelAsync();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => running);
		await AssertAllExitedAsync(started);
	}

	/// <summary>
	/// Waits for the grandchild the sleeper starts, and reports its process ids.
	/// </summary>
	/// <remarks>
	/// The grandchild rather than the direct child, because killing only the process that was started
	/// is exactly the failure this is looking for: git delegates its transport to a helper, and a
	/// helper left holding a connection is the leak that matters.
	/// </remarks>
	private static async Task<int[]> WaitForNewChildAsync(int[] before)
	{
		for (int attempt = 0; attempt < 100; attempt++)
		{
			int[] started = [.. ProcessIds(SleeperChildName).Except(before)];

			if (started.Length > 0)
			{
				return started;
			}

			await Task.Delay(50);
		}

		Assert.Fail($"No '{SleeperChildName}' process started, so there is nothing to assert was reaped.");
		return [];
	}

	private static async Task AssertAllExitedAsync(int[] processIds)
	{
		foreach (int processId in processIds)
		{
			for (int attempt = 0; attempt < 100; attempt++)
			{
				if (!ProcessIds(SleeperChildName).Contains(processId))
				{
					break;
				}

				await Task.Delay(50);
			}

			Assert.DoesNotContain(processId, ProcessIds(SleeperChildName));
		}
	}
}
