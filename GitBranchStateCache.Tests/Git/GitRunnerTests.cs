// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Git;

using System.Collections;
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
	public void BuildEnvironment_SetsTheFlagsThatKeepARunPredictable()
	{
		Dictionary<string, string?> environment = GitRunner.BuildEnvironment(
			new GitInvocation { Arguments = ["--version"], Timeout = TimeSpan.FromSeconds(1) },
			new GitBranchStateCacheOptions { MirrorRoot = TempRoot },
			new Hashtable { ["GIT_DIR"] = "/somewhere/inherited", ["PATH"] = "/usr/bin" });

		// GIT_NO_LAZY_FETCH turns a demand for filtered content into a visible error rather than an
		// enormous unplanned fetch, and no terminal prompt turns a missing credential into a refusal
		// rather than a process waiting on a terminal that is not there.
		Assert.AreEqual("1", environment["GIT_NO_LAZY_FETCH"]);
		Assert.AreEqual("0", environment["GIT_TERMINAL_PROMPT"]);
		Assert.AreEqual("1", environment["GIT_CONFIG_NOSYSTEM"]);

		// An inherited GIT_DIR would point every run at a repository nobody asked for. A null value in
		// the overlay is what removes it from the child's environment.
		Assert.IsTrue(environment.TryGetValue("GIT_DIR", out string? gitDir));
		Assert.IsNull(gitDir);

		// Everything else is inherited untouched, so it is left out of the overlay.
		Assert.IsFalse(environment.ContainsKey("PATH"));
	}

	[TestMethod]
	public void BuildEnvironment_WithACredential_ScopesItToTheUpstream()
	{
		const string credential = "Basic dXNlcjp0b2tlbg==";

		Dictionary<string, string?> environment = GitRunner.BuildEnvironment(
			new GitInvocation
			{
				Arguments = ["ls-remote", "https://github.com/studio/game.git"],
				CredentialScope = new Uri("https://github.com"),
				Authorization = credential,
				Timeout = TimeSpan.FromSeconds(1),
			},
			new GitBranchStateCacheOptions { MirrorRoot = TempRoot },
			new Hashtable());

		// Scoped to the upstream rather than set for all of http, because git matches this
		// configuration by URL prefix and a redirect leading off the forge would otherwise carry the
		// caller's credential with it.
		Assert.AreEqual("2", environment["GIT_CONFIG_COUNT"]);
		Assert.AreEqual("http.https://github.com/.extraHeader", environment["GIT_CONFIG_KEY_1"]);
		Assert.AreEqual($"Authorization: {credential}", environment["GIT_CONFIG_VALUE_1"]);
	}

	[TestMethod]
	public void BuildEnvironment_WithoutACredential_SetsNoHeader()
	{
		Dictionary<string, string?> environment = GitRunner.BuildEnvironment(
			new GitInvocation { Arguments = ["--version"], Timeout = TimeSpan.FromSeconds(1) },
			new GitBranchStateCacheOptions { MirrorRoot = TempRoot },
			new Hashtable());

		Assert.AreEqual("1", environment["GIT_CONFIG_COUNT"]);
		Assert.AreEqual("credential.helper", environment["GIT_CONFIG_KEY_0"]);
	}

	[TestMethod]
	public async Task RunAsync_ACommandThatReadsStandardInput_SeesEndOfStreamRatherThanWaiting()
	{
		// git is never fed anything, so a child that reads standard input has to find it closed. Left
		// inherited, it would wait on whatever this service's own standard input is, which under a
		// service manager can be a pipe nobody writes to, and the run would end only at its timeout.
		GitResult result = await Build(ReaderExecutable()).RunAsync(
			new GitInvocation { Arguments = ReaderArguments(), Timeout = TimeSpan.FromSeconds(20) },
			CancellationToken.None);

		Assert.IsFalse(result.TimedOut, "The command waited on standard input until it was killed.");
		Assert.IsTrue(result.Succeeded, result.StandardError);
		Assert.Contains("eof", result.StandardOutput);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	[DataRow("before\\n\\377\\376\\nafter\\n", DisplayName = "invalid bytes among valid text")]
	[DataRow("\\377\\376", DisplayName = "invalid bytes alone")]
	public async Task RunAsync_OutputThatIsNotUtf8_IsReportedRatherThanReadAsEmptyOrReplaced(string printfFormat)
	{
		// An undecodable branch or path name must fail loudly. Read leniently it becomes a name that
		// matches nothing, and read as nothing it becomes "no branches", both of which look like
		// answers. POSIX only, because it needs a shell that can write raw bytes.
		GitResult result = await Build("/bin/sh").RunAsync(
			new GitInvocation { Arguments = ["-c", $"printf '{printfFormat}'"], Timeout = TimeSpan.FromSeconds(20) },
			CancellationToken.None);

		Assert.IsFalse(result.Succeeded);
		Assert.IsFalse(result.TimedOut);
		Assert.Contains("not valid UTF-8", result.StandardError);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public async Task RunAsync_OutputThatIsNotUtf8AndLargerThanThePipe_IsReportedPromptlyRatherThanAsATimeout()
	{
		// A diff-tree with one non-UTF-8 path among enough others to fill the pipe. Once the decoder
		// refuses the bad byte, the rest still has to be drained or the process ended, or git blocks on
		// the full pipe and the run is reported as a timeout instead (ktsu-dev/GitBranchStateCache#50).
		Stopwatch elapsed = Stopwatch.StartNew();

		GitResult result = await Build("/bin/sh").RunAsync(
			new GitInvocation
			{
				Arguments = ["-c", "printf 'b\\377.uasset\\0'; head -c 204800 /dev/zero | tr '\\0' a"],
				Timeout = TimeSpan.FromSeconds(20),
			},
			CancellationToken.None);

		Assert.IsFalse(result.TimedOut, "The run stalled on a full pipe until it was killed.");
		Assert.Contains("not valid UTF-8", result.StandardError);
		Assert.IsLessThan(TimeSpan.FromSeconds(10), elapsed.Elapsed);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public async Task RunAsync_DiscardingStandardOutput_SucceedsWhateverTheOutputHolds()
	{
		// The admission probe asks only whether ls-remote succeeded. A branch name git allows but
		// UTF-8 cannot read must not turn a reachable repository into a refused one.
		GitResult result = await Build("/bin/sh").RunAsync(
			new GitInvocation
			{
				Arguments = ["-c", "printf 'deadbeef\\trefs/heads/caf\\351\\n'; head -c 204800 /dev/zero | tr '\\0' a"],
				Timeout = TimeSpan.FromSeconds(20),
				DiscardStandardOutput = true,
			},
			CancellationToken.None);

		Assert.IsTrue(result.Succeeded, result.StandardError);
		Assert.AreEqual(string.Empty, result.StandardOutput);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public async Task RunAsync_DiscardingStandardOutput_StillReportsStandardError()
	{
		// What a refused probe says on standard error is what classifies it, so only standard output
		// is thrown away.
		GitResult result = await Build("/bin/sh").RunAsync(
			new GitInvocation
			{
				Arguments = ["-c", "printf 'fatal: Authentication failed\\n' >&2; exit 128"],
				Timeout = TimeSpan.FromSeconds(20),
				DiscardStandardOutput = true,
			},
			CancellationToken.None);

		Assert.IsFalse(result.Succeeded);
		Assert.Contains("Authentication failed", result.StandardError);
	}

	private static string ReaderExecutable() => OnWindows ? "cmd.exe" : "/bin/sh";

	private static string[] ReaderArguments() => OnWindows
		? ["/c", "set /p line= & echo eof"]
		: ["-c", "if read line; then echo \"read:$line\"; else echo eof; fi"];

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
