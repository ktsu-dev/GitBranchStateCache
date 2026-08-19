// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Mirrors;

using System.Diagnostics.Metrics;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testably.Abstractions.Testing;

[TestClass]
public class MirrorMaintenanceServiceTests
{
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
