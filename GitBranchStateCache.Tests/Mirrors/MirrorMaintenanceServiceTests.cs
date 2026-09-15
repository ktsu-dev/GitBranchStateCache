// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Mirrors;

using System.Diagnostics.Metrics;
using ktsu.GitBranchStateCache.Coalescing;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Observability;
using ktsu.GitBranchStateCache.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testably.Abstractions.Testing;

[TestClass]
public class MirrorMaintenanceServiceTests
{
	/// <summary>Gets or sets the context MSTest supplies, used for the run's cancellation token.</summary>
	public TestContext TestContext { get; set; } = null!;

	private static readonly string Root = Path.Combine(
		Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
		"gitbranchstatecache-sweep");

	private static (MirrorMaintenanceService Service, MirrorStore Store, MockFileSystem FileSystem, FakeTimeProvider Time)
		Build(TimeSpan? idleMaxAge = null)
	{
		MockFileSystem fileSystem = new();
		fileSystem.Directory.CreateDirectory(Root);

		FakeTimeProvider time = new(new DateTimeOffset(2026, 8, 19, 9, 47, 0, TimeSpan.Zero));
		IOptions<GitBranchStateCacheOptions> options = Options.Create(new GitBranchStateCacheOptions
		{
			MirrorRoot = Root,
			MirrorIdleMaxAge = idleMaxAge ?? TimeSpan.FromDays(30),
		});

		MirrorStore store = new(fileSystem, options, time);

		ServiceCollection services = new();
		services.AddMetrics();
		IMeterFactory meterFactory = services.BuildServiceProvider().GetRequiredService<IMeterFactory>();

		MirrorMaintenanceService service = new(
			store,
			fileSystem,
			new BranchStateMetrics(meterFactory),
			options,
			time,
			NullLogger<MirrorMaintenanceService>.Instance);

		return (service, store, fileSystem, time);
	}

	/// <summary>
	/// Builds a fetcher sharing the sweep's store, filesystem and clock, so the two race for real.
	/// </summary>
	private static MirrorFetcher BuildFetcher(
		FakeGitRunner runner,
		MirrorStore store,
		MockFileSystem fileSystem,
		FakeTimeProvider time)
	{
		IOptions<GitBranchStateCacheOptions> options = Options.Create(new GitBranchStateCacheOptions
		{
			MirrorRoot = Root,
		});

		ServiceCollection services = new();
		services.AddMetrics();
		IMeterFactory meterFactory = services.BuildServiceProvider().GetRequiredService<IMeterFactory>();

		return new MirrorFetcher(
			runner,
			store,
			fileSystem,
			new SingleFlight(),
			new BranchStateMetrics(meterFactory),
			options,
			time,
			NullLogger<MirrorFetcher>.Instance);
	}

	private static string Seed(MirrorStore store, MockFileSystem fileSystem, string repositoryPath)
	{
		store.TryResolve(new MirrorKey("github", repositoryPath), out string? directory);
		fileSystem.Directory.CreateDirectory(fileSystem.Path.Combine(directory!, "objects"));
		fileSystem.File.WriteAllText(fileSystem.Path.Combine(directory!, "objects", "pack"), "some bytes");
		return directory!;
	}

	[TestMethod]
	public void Sweep_AMirrorStillBeingQueried_IsKept()
	{
		(MirrorMaintenanceService service, MirrorStore store, MockFileSystem fileSystem, FakeTimeProvider time) =
			Build(TimeSpan.FromDays(30));

		string directory = Seed(store, fileSystem, "studio/game.git");
		store.MarkUsed(directory);
		time.Advance(TimeSpan.FromDays(29));

		service.Sweep();

		Assert.IsTrue(store.Exists(directory));
	}

	[TestMethod]
	public void Sweep_AMirrorNobodyHasQueriedForTooLong_IsRemoved()
	{
		// The allow-list bounds which repositories may ever be mirrored, but not for how long. Without
		// this, disk use only ever ratchets upwards.
		(MirrorMaintenanceService service, MirrorStore store, MockFileSystem fileSystem, FakeTimeProvider time) =
			Build(TimeSpan.FromDays(30));

		string directory = Seed(store, fileSystem, "studio/game.git");
		store.MarkUsed(directory);
		time.Advance(TimeSpan.FromDays(31));

		service.Sweep();

		// Deleting is the cheapest possible way to be wrong: the next request clones it again.
		Assert.IsFalse(store.Exists(directory));
	}

	[TestMethod]
	public void Sweep_WithReapingDisabled_KeepsEverything()
	{
		(MirrorMaintenanceService service, MirrorStore store, MockFileSystem fileSystem, FakeTimeProvider time) =
			Build(TimeSpan.Zero);

		string directory = Seed(store, fileSystem, "studio/game.git");
		store.MarkUsed(directory);
		time.Advance(TimeSpan.FromDays(3650));

		service.Sweep();

		Assert.IsTrue(store.Exists(directory));
	}

	[TestMethod]
	public void Sweep_AMirrorWithNoMarkers_FallsBackToWhenItWasCreated()
	{
		// A mirror created by a deployment that predates the markers would otherwise look infinitely
		// old and be deleted on the first sweep after an upgrade.
		(MirrorMaintenanceService service, MirrorStore store, MockFileSystem fileSystem, _) =
			Build(TimeSpan.FromDays(30));

		string directory = Seed(store, fileSystem, "studio/game.git");

		service.Sweep();

		Assert.IsTrue(store.Exists(directory));
	}

	[TestMethod]
	public async Task Sweep_WhileAFetchIsRunningAgainstAnIdleMirror_KeepsTheMirrorAndLetsTheFetchFinishAsync()
	{
		// A fetch of a large repository can run for the whole of FetchTimeout, and the sweep ticks on its
		// own schedule. If the mirror only records the fetch once it succeeds, a sweep landing in that
		// window reads the pre-fetch markers, judges the mirror idle, and recursively deletes the very
		// directory git has open as its working directory.
		(MirrorMaintenanceService service, MirrorStore store, MockFileSystem fileSystem, FakeTimeProvider time) =
			Build(TimeSpan.FromDays(30));

		string directory = Seed(store, fileSystem, "studio/game.git");
		store.MarkUsed(directory);
		store.MarkFetched(directory);
		time.Advance(TimeSpan.FromDays(31));

		TaskCompletionSource fetchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource fetchMayFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);

		FakeGitRunner runner = new()
		{
			Before = async _ =>
			{
				fetchStarted.TrySetResult();
				await fetchMayFinish.Task.ConfigureAwait(false);
			},
		};

		MirrorFetcher fetcher = BuildFetcher(runner, store, fileSystem, time);

		Task<MirrorFetchResult> fetch = fetcher.EnsureCurrentAsync(
			new MirrorKey("github", "studio/game.git"),
			directory,
			new Uri("https://forge.example/studio/game.git"),
			new Uri("https://forge.example"),
			authorization: null,
			TestContext.CancellationTokenSource.Token);

		await fetchStarted.Task.ConfigureAwait(false);

		service.Sweep();

		// Checked while the fetch is still blocked. A later check would not see the reap: writing the
		// fetch marker recreates the directory, so the mirror comes back empty rather than missing.
		bool survived = fileSystem.File.Exists(fileSystem.Path.Combine(directory, "objects", "pack"));

		fetchMayFinish.TrySetResult();
		MirrorFetchResult result = await fetch.ConfigureAwait(false);

		Assert.IsTrue(survived, "The sweep reaped a mirror that a fetch had open as its working directory.");
		Assert.AreEqual(MirrorFetchStatus.Current, result.Status);
		Assert.AreEqual(1, runner.CountOf("fetch"));
	}

	[TestMethod]
	public void Sweep_AMirrorFetchedRecentlyButNotYetRecordedAsUsed_IsKept()
	{
		// A request whose refs are already current returns without fetching and records its own use only
		// once it has been answered. In that window the fetch marker is fresh and the use marker is not,
		// so a sweep reading only the use marker reaps a mirror that is being read from right now.
		(MirrorMaintenanceService service, MirrorStore store, MockFileSystem fileSystem, FakeTimeProvider time) =
			Build(TimeSpan.FromDays(30));

		string directory = Seed(store, fileSystem, "studio/game.git");
		store.MarkUsed(directory);
		time.Advance(TimeSpan.FromDays(31));
		store.MarkFetched(directory);

		service.Sweep();

		Assert.IsTrue(store.Exists(directory));
	}

	[TestMethod]
	public void Sweep_LeavesTheMirrorsThatAreStillWanted()
	{
		(MirrorMaintenanceService service, MirrorStore store, MockFileSystem fileSystem, FakeTimeProvider time) =
			Build(TimeSpan.FromDays(30));

		string idle = Seed(store, fileSystem, "studio/old.git");
		store.MarkUsed(idle);
		time.Advance(TimeSpan.FromDays(31));

		string busy = Seed(store, fileSystem, "studio/game.git");
		store.MarkUsed(busy);

		service.Sweep();

		Assert.IsFalse(store.Exists(idle));
		Assert.IsTrue(store.Exists(busy));
	}
}
