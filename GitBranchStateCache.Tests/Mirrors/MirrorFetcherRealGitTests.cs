// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Mirrors;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Abstractions;
using System.Text;
using ktsu.Essentials.FileSystemProviders.Native;
using ktsu.GitBranchStateCache.Coalescing;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Observability;
using ktsu.GitBranchStateCache.Refs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Exercises cloning and fetching against a real repository over a real transport.
/// </summary>
/// <remarks>
/// The one part of the mirror subsystem a fake cannot check. A bare clone has no fetch refspec, so
/// without the configuration step that follows it a mirror would be created correctly, answer the
/// first request correctly, and then be frozen at the moment it was created for the rest of its life.
/// Nothing short of a real fetch against a repository that has moved catches that.
/// </remarks>
[TestClass]
public class MirrorFetcherRealGitTests
{
	private static readonly Uri UpstreamBase = new("https://not-used.example");

	private string _root = string.Empty;
	private string _source = string.Empty;

	private GitBranchStateCacheOptions Settings => new()
	{
		MirrorRoot = _root,
		RefsTtl = TimeSpan.FromSeconds(30),
		FetchTimeout = TimeSpan.FromMinutes(2),
		ProbeTimeout = TimeSpan.FromSeconds(60),
	};

	[TestInitialize]
	public void BuildSourceRepository()
	{
		_root = Path.Combine(Path.GetTempPath(), $"gbsc-fetch-{Guid.NewGuid():N}");
		_source = Path.Combine(_root, "source");

		Directory.CreateDirectory(_source);
		Directory.CreateDirectory(Path.Combine(_root, "mirrors"));

		Git(_source, "init", "--initial-branch=main");
		Commit("Content/Maps/Foo.umap", "one", "initial");
	}

	[TestCleanup]
	public void RemoveFixture()
	{
		if (!Directory.Exists(_root))
		{
			return;
		}

		try
		{
			// Git marks objects read-only, which Directory.Delete refuses on Windows.
			foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(file, FileAttributes.Normal);
			}

			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// A leftover fixture in the temp directory is not worth failing a run over.
		}
	}

	private (MirrorFetcher Fetcher, MirrorStore Store, FakeTimeProvider Time, string Directory) Build()
	{
		IOptions<GitBranchStateCacheOptions> options = Options.Create(Settings);
		IFileSystem fileSystem = new NativeFileSystemProvider();
		FakeTimeProvider time = new(new DateTimeOffset(2026, 8, 19, 9, 47, 0, TimeSpan.Zero));
		MirrorStore store = new(fileSystem, options, time);

		ServiceCollection services = new();
		services.AddMetrics();
		IMeterFactory meterFactory = services.BuildServiceProvider().GetRequiredService<IMeterFactory>();

		MirrorFetcher fetcher = new(
			new GitRunner(options),
			store,
			fileSystem,
			new SingleFlight(),
			new BranchStateMetrics(meterFactory),
			options,
			time,
			NullLogger<MirrorFetcher>.Instance);

		Assert.IsTrue(store.TryResolve(new MirrorKey("github", "studio/game.git"), out string? directory));
		return (fetcher, store, time, directory!);
	}

	private Task<MirrorFetchResult> EnsureAsync(MirrorFetcher fetcher, string directory) =>
		fetcher.EnsureCurrentAsync(
			new MirrorKey("github", "studio/game.git"),
			directory,

			// A file URL rather than a bare path, so git uses its real transport rather than the
			// hardlink shortcut it takes for a local directory.
			new Uri($"file:///{_source.Replace('\\', '/')}"),
			UpstreamBase,
			authorization: null,
			CancellationToken.None);

	private async Task<IReadOnlyList<BranchRef>> BranchesAsync(string directory)
	{
		IReadOnlyList<BranchRef>? branches = await new RefResolver(new GitRunner(Options.Create(Settings)), Options.Create(Settings))
			.ListAsync(directory, CancellationToken.None);

		Assert.IsNotNull(branches);
		return branches!;
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_FirstRequest_ClonesAMirrorAndRecordsWhen()
	{
		(MirrorFetcher fetcher, MirrorStore store, FakeTimeProvider time, string directory) = Build();

		MirrorFetchResult result = await EnsureAsync(fetcher, directory);

		Assert.AreEqual(MirrorFetchStatus.Current, result.Status, result.Failure);
		Assert.IsTrue(store.Exists(directory));
		Assert.AreEqual(time.GetUtcNow(), store.RefsFetchedAt(directory));

		BranchRef main = (await BranchesAsync(directory)).Single(branch => branch.Name == "origin/main");
		Assert.AreEqual(Git(_source, "rev-parse", "HEAD").Trim(), main.Tip);
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_LeavesNoStagingDirectoryBehind()
	{
		// A clone lands in a staging directory and is moved into place only once it has finished, so a
		// crash part way through a large clone cannot leave something that looks like a mirror.
		(MirrorFetcher fetcher, _, _, string directory) = Build();

		await EnsureAsync(fetcher, directory);

		string parent = Path.GetDirectoryName(directory)!;
		Assert.IsEmpty(Directory.GetDirectories(parent, "mirror.git.tmp-*"));
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_WithinTheRefsTtl_DoesNotTouchTheUpstreamAgain()
	{
		(MirrorFetcher fetcher, _, _, string directory) = Build();

		await EnsureAsync(fetcher, directory);
		Commit("Content/Maps/Foo.umap", "two", "second");

		MirrorFetchResult second = await EnsureAsync(fetcher, directory);

		Assert.AreEqual(MirrorFetchStatus.Current, second.Status);

		// Still the commit the clone captured, because nothing asked the upstream anything.
		BranchRef main = (await BranchesAsync(directory)).Single(branch => branch.Name == "origin/main");
		Assert.AreNotEqual(Git(_source, "rev-parse", "HEAD").Trim(), main.Tip);
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_AfterTheRefsTtl_FetchesWhatTheUpstreamHasSince()
	{
		// The assertion this whole class exists for. A bare clone has no fetch refspec, so without the
		// configuration step the mirror would answer this exactly as it did before, forever.
		(MirrorFetcher fetcher, _, FakeTimeProvider time, string directory) = Build();

		await EnsureAsync(fetcher, directory);
		Commit("Content/Maps/Foo.umap", "two", "second");
		string moved = Git(_source, "rev-parse", "HEAD").Trim();

		time.Advance(TimeSpan.FromMinutes(1));
		MirrorFetchResult second = await EnsureAsync(fetcher, directory);

		Assert.AreEqual(MirrorFetchStatus.Current, second.Status, second.Failure);
		Assert.AreEqual(time.GetUtcNow(), second.RefsAsOf);

		BranchRef main = (await BranchesAsync(directory)).Single(branch => branch.Name == "origin/main");
		Assert.AreEqual(moved, main.Tip);
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_ABranchDeletedUpstream_IsPrunedRatherThanKept()
	{
		// Without --prune a branch that was deleted upstream would keep being reported, and a client
		// would keep being warned about changes on a branch nobody can merge any more.
		(MirrorFetcher fetcher, _, FakeTimeProvider time, string directory) = Build();

		Git(_source, "branch", "feature/gone");
		await EnsureAsync(fetcher, directory);
		Assert.IsTrue((await BranchesAsync(directory)).Any(branch => branch.Name == "origin/feature/gone"));

		Git(_source, "branch", "-D", "feature/gone");
		time.Advance(TimeSpan.FromMinutes(1));
		await EnsureAsync(fetcher, directory);

		Assert.IsFalse((await BranchesAsync(directory)).Any(branch => branch.Name == "origin/feature/gone"));
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_WhenTheUpstreamDoesNotExist_ReportsNoMirrorRatherThanAnEmptyOne()
	{
		// A repository this service cannot clone must never look like a repository with no branches.
		(MirrorFetcher fetcher, MirrorStore store, _, string directory) = Build();

		MirrorFetchResult result = await fetcher.EnsureCurrentAsync(
			new MirrorKey("github", "studio/game.git"),
			directory,
			new Uri($"file:///{_source.Replace('\\', '/')}-does-not-exist"),
			UpstreamBase,
			authorization: null,
			CancellationToken.None);

		Assert.AreEqual(MirrorFetchStatus.Unavailable, result.Status);
		Assert.IsFalse(store.Exists(directory));
	}

	private void Commit(string relativePath, string content, string message)
	{
		string full = Path.Combine(_source, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, content, new UTF8Encoding(false));

		Git(_source, "add", "-A");
		Git(_source, "-c", "user.email=tests@ktsu.dev", "-c", "user.name=tests", "commit", "--quiet", "-m", message);
	}

	/// <summary>
	/// Runs git for fixture setup, with its own process handling.
	/// </summary>
	/// <remarks>
	/// Deliberately not <see cref="GitRunner"/>. The fixture needs a committer identity and a writable
	/// working tree, neither of which the runner permits, and a fixture that shares the implementation
	/// it is meant to check would not be checking anything.
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
