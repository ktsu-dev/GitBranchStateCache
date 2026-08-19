// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Git;

using System.Diagnostics;
using System.Text;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Diffs;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Refs;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Exercises the git-touching units against a real bare repository.
/// </summary>
/// <remarks>
/// Everything above these units is tested with a fake runner, which is what keeps the rest of the
/// suite fast. These exist because a fake cannot tell anyone whether the arguments this service
/// builds mean what it thinks they mean, and the answer to that question is the whole product.
/// </remarks>
[TestClass]
public class RealRepositoryTests
{
	private const string AwkwardPath = "Content/Personnages/Épée de feu.uasset";

	private static string _workTree = string.Empty;
	private static string _mirror = string.Empty;
	private static string _mainTip = string.Empty;
	private static string _featureTip = string.Empty;
	private static string _forkPoint = string.Empty;

	private static GitBranchStateCacheOptions Settings => new()
	{
		MirrorRoot = Path.GetDirectoryName(_mirror)!,
		ProbeTimeout = TimeSpan.FromSeconds(60),
		DiffTimeout = TimeSpan.FromSeconds(60),
	};

	private static GitRunner Runner => new(Options.Create(Settings));

	[ClassInitialize]
	public static void BuildFixture(TestContext context)
	{
		string root = Path.Combine(Path.GetTempPath(), $"gbsc-fixture-{Guid.NewGuid():N}");
		_workTree = Path.Combine(root, "work");
		_mirror = Path.Combine(root, "mirror.git");

		Directory.CreateDirectory(_workTree);

		Git(_workTree, "init", "--initial-branch=main");
		Write("Content/Maps/Foo.umap", "one");
		Write("Content/Chars/Bar.uasset", "bar");
		Write(AwkwardPath, "sword");
		Git(_workTree, "add", "-A");
		Commit("initial");
		_forkPoint = Git(_workTree, "rev-parse", "HEAD").Trim();

		Git(_workTree, "checkout", "-b", "feature/ui");
		Write("Content/Chars/Bar.uasset", "bar changed on the feature branch");
		Write("Content/Maps/New.umap", "added on the feature branch");
		File.Delete(Path.Combine(_workTree, AwkwardPath.Replace('/', Path.DirectorySeparatorChar)));
		Git(_workTree, "add", "-A");
		Commit("feature work");
		_featureTip = Git(_workTree, "rev-parse", "HEAD").Trim();

		Git(_workTree, "checkout", "main");
		Write("README.md", "unrelated change on main");
		Git(_workTree, "add", "-A");
		Commit("main work");
		_mainTip = Git(_workTree, "rev-parse", "HEAD").Trim();

		Git(root, "clone", "--bare", "--quiet", _workTree, _mirror);
	}

	[ClassCleanup]
	public static void RemoveFixture()
	{
		string? root = Path.GetDirectoryName(_mirror);

		if (root is not null && Directory.Exists(root))
		{
			try
			{
				// Git marks objects read-only, which Directory.Delete refuses on Windows.
				foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
				{
					File.SetAttributes(file, FileAttributes.Normal);
				}

				Directory.Delete(root, recursive: true);
			}
			catch (IOException)
			{
				// A leftover fixture in the temp directory is not worth failing a run over.
			}
		}
	}

	[TestMethod]
	public async Task RefResolver_ReadsEveryBranchWithItsTip()
	{
		IReadOnlyList<BranchRef>? branches = await new RefResolver(Runner, Options.Create(Settings))
			.ListAsync(_mirror, CancellationToken.None);

		Assert.IsNotNull(branches);
		Assert.IsTrue(branches!.Any(branch => branch.Name == "origin/main" && branch.Tip == _mainTip));
		Assert.IsTrue(branches!.Any(branch => branch.Name == "origin/feature/ui" && branch.Tip == _featureTip));
	}

	[TestMethod]
	public async Task MirrorQueries_FindsTheForkPointAsTheMergeBase()
	{
		MirrorQueries queries = new(Runner, Options.Create(Settings));

		string? mergeBase = await queries.FindMergeBaseAsync(_mirror, _mainTip, _featureTip, CancellationToken.None);

		Assert.AreEqual(_forkPoint, mergeBase);
	}

	[TestMethod]
	public async Task MirrorQueries_ContainsCommit_IsTrueForACommitTheMirrorHolds()
	{
		MirrorQueries queries = new(Runner, Options.Create(Settings));

		Assert.IsTrue(await queries.ContainsCommitAsync(_mirror, _mainTip, CancellationToken.None));
	}

	[TestMethod]
	public async Task MirrorQueries_ContainsCommit_IsFalseForACommitItDoesNot()
	{
		// This is what produces the unknown-base answer, for a client that has not pushed in a long
		// time or has rewritten history.
		MirrorQueries queries = new(Runner, Options.Create(Settings));

		Assert.IsFalse(await queries.ContainsCommitAsync(
			_mirror,
			"0123456789abcdef0123456789abcdef01234567",
			CancellationToken.None));
	}

	[TestMethod]
	public async Task MirrorQueries_ContainsCommit_RefusesAnythingThatIsNotAnObjectId()
	{
		// git accepts a great deal more than object ids where one is expected, and none of it is
		// something a client has any business asking about.
		MirrorQueries queries = new(Runner, Options.Create(Settings));

		Assert.IsFalse(await queries.ContainsCommitAsync(_mirror, "HEAD", CancellationToken.None));
		Assert.IsFalse(await queries.ContainsCommitAsync(_mirror, "--help", CancellationToken.None));
	}

	[TestMethod]
	public async Task DiffSource_ReportsModificationsAdditionsAndDeletionsWithRealBlobIds()
	{
		DiffOutcome outcome = await new DiffSource(Runner, Options.Create(Settings))
			.ComputeAsync(_mirror, _forkPoint, _featureTip, CancellationToken.None);

		Assert.IsTrue(outcome.Succeeded, outcome.Failure);
		IReadOnlyList<DiffEntry> entries = outcome.Entries!;

		DiffEntry modified = entries.Single(entry => entry.Path == "Content/Chars/Bar.uasset");
		Assert.AreEqual('M', modified.Status);
		Assert.IsNotNull(modified.Blob);

		// The blob id has to be the one git itself would report for that content at that commit, or a
		// client comparing against its own working tree gets a mismatch for a file that is identical.
		string expected = Git(_workTree, "rev-parse", $"{_featureTip}:Content/Chars/Bar.uasset").Trim();
		Assert.AreEqual(expected, modified.Blob);

		DiffEntry added = entries.Single(entry => entry.Path == "Content/Maps/New.umap");
		Assert.AreEqual('A', added.Status);

		DiffEntry deleted = entries.Single(entry => entry.Path == AwkwardPath);
		Assert.AreEqual('D', deleted.Status);
		Assert.IsNull(deleted.Blob);
	}

	[TestMethod]
	public async Task DiffSource_NonAsciiPathWithASpace_SurvivesTheRoundTrip()
	{
		// In git's default raw format this path comes back quoted and octal-escaped. The -z form is
		// what makes it arrive verbatim, and a path that arrives mangled matches nothing on the client
		// and silently fails to warn about a stale asset.
		DiffOutcome outcome = await new DiffSource(Runner, Options.Create(Settings))
			.ComputeAsync(_mirror, _forkPoint, _featureTip, CancellationToken.None);

		Assert.IsTrue(outcome.Entries!.Any(entry => entry.Path == AwkwardPath));
	}

	[TestMethod]
	public async Task DiffSource_BetweenACommitAndItself_IsNoChanges()
	{
		DiffOutcome outcome = await new DiffSource(Runner, Options.Create(Settings))
			.ComputeAsync(_mirror, _mainTip, _mainTip, CancellationToken.None);

		Assert.IsTrue(outcome.Succeeded, outcome.Failure);
		Assert.IsEmpty(outcome.Entries!);
	}

	private static void Write(string relativePath, string content)
	{
		string full = Path.Combine(_workTree, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, content, new UTF8Encoding(false));
	}

	private static void Commit(string message) =>
		Git(
			_workTree,
			"-c",
			"user.email=tests@ktsu.dev",
			"-c",
			"user.name=tests",
			"commit",
			"--quiet",
			"-m",
			message);

	/// <summary>
	/// Runs git for fixture setup, with its own process handling.
	/// </summary>
	/// <remarks>
	/// Deliberately not <see cref="GitRunner"/>. The fixture needs a committer identity and a
	/// writable working tree, neither of which the runner permits, and a fixture that shares the
	/// implementation it is meant to check would not be checking anything.
	/// </remarks>
	private static string Git(string workingDirectory, params string[] arguments)
	{
		ProcessStartInfo startInfo = new()
		{
			FileName = "git",
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = new UTF8Encoding(false),
			StandardErrorEncoding = new UTF8Encoding(false),
		};

		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("git could not be started.");

		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();

		return process.ExitCode == 0
			? output
			: throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
	}
}
