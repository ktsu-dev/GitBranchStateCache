// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Mirrors;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Mirrors;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testably.Abstractions.Testing;

[TestClass]
public class MirrorStoreTests
{
	private static readonly string Root = Path.Combine(
		Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
		"gitbranchstatecache");

	private static (MirrorStore Store, MockFileSystem FileSystem, FakeTimeProvider Time) Build()
	{
		MockFileSystem fileSystem = new();
		fileSystem.Directory.CreateDirectory(Root);

		FakeTimeProvider time = new(new DateTimeOffset(2026, 8, 19, 9, 47, 0, TimeSpan.Zero));
		GitBranchStateCacheOptions options = new() { MirrorRoot = Root };

		return (new MirrorStore(fileSystem, Options.Create(options), time), fileSystem, time);
	}

	[TestMethod]
	public void TryResolve_LaysTheRepositoryOutAsDirectories()
	{
		(MirrorStore store, MockFileSystem fileSystem, _) = Build();

		Assert.IsTrue(store.TryResolve(new MirrorKey("github", "studio/game.git"), out string? directory));

		string expected = fileSystem.Path.Combine(Root, "github", "studio", "game.git", "mirror.git");
		Assert.AreEqual(expected, directory);
	}

	[TestMethod]
	[DataRow("..")]
	[DataRow("studio/..")]
	[DataRow("../escape")]
	[DataRow(".hidden/repo")]
	[DataRow("studio/re po")]
	[DataRow("studio/re:po")]
	[DataRow("-dashed/repo")]
	public void TryResolve_UnsafeRepositoryPath_IsRefused(string repositoryPath)
	{
		// The allow-list has already refused anything not explicitly configured. This is the second
		// line, and it exists because a traversal that reaches the filesystem is not the place to
		// discover that the first line had a gap.
		(MirrorStore store, _, _) = Build();

		Assert.IsFalse(store.TryResolve(new MirrorKey("github", repositoryPath), out _));
	}

	[TestMethod]
	public void TryResolve_UnsafeUpstreamKey_IsRefused()
	{
		(MirrorStore store, _, _) = Build();

		Assert.IsFalse(store.TryResolve(new MirrorKey("..", "studio/game.git"), out _));
	}

	[TestMethod]
	public void RefsFetchedAt_BeforeAnyFetch_IsNull()
	{
		(MirrorStore store, _, _) = Build();
		store.TryResolve(new MirrorKey("github", "studio/game.git"), out string? directory);

		Assert.IsNull(store.RefsFetchedAt(directory!));
	}

	[TestMethod]
	public void MarkFetched_ThenRefsFetchedAt_ReportsWhen()
	{
		// Recorded rather than inferred from a modification time. A fetch that finds nothing new
		// touches nothing, so a perfectly current mirror would otherwise look steadily more stale and
		// be refetched forever.
		(MirrorStore store, _, FakeTimeProvider time) = Build();
		store.TryResolve(new MirrorKey("github", "studio/game.git"), out string? directory);

		store.MarkFetched(directory!);

		Assert.AreEqual(time.GetUtcNow(), store.RefsFetchedAt(directory!));
	}

	[TestMethod]
	public void MarkUsed_IsRecordedSeparatelyFromFetching()
	{
		(MirrorStore store, _, FakeTimeProvider time) = Build();
		store.TryResolve(new MirrorKey("github", "studio/game.git"), out string? directory);

		store.MarkFetched(directory!);
		time.Advance(TimeSpan.FromHours(2));
		store.MarkUsed(directory!);

		Assert.AreNotEqual(store.RefsFetchedAt(directory!), store.LastUsedAt(directory!));
	}

	[TestMethod]
	public void Enumerate_FindsEveryMirrorAtAnyDepth()
	{
		(MirrorStore store, MockFileSystem fileSystem, _) = Build();

		fileSystem.Directory.CreateDirectory(fileSystem.Path.Combine(Root, "github", "studio", "game.git", "mirror.git"));
		fileSystem.Directory.CreateDirectory(fileSystem.Path.Combine(Root, "ado", "proj", "_git", "game", "mirror.git"));

		Assert.HasCount(2, store.Enumerate());
	}

	[TestMethod]
	public void Enumerate_WhenTheRootIsMissing_IsEmpty()
	{
		MockFileSystem fileSystem = new();
		MirrorStore store = new(
			fileSystem,
			Options.Create(new GitBranchStateCacheOptions { MirrorRoot = Root }),
			new FakeTimeProvider());

		Assert.IsEmpty(store.Enumerate());
	}

	[TestMethod]
	public void Delete_RemovesTheMirror()
	{
		(MirrorStore store, MockFileSystem fileSystem, _) = Build();
		store.TryResolve(new MirrorKey("github", "studio/game.git"), out string? directory);
		fileSystem.Directory.CreateDirectory(fileSystem.Path.Combine(directory!, "objects"));

		store.Delete(directory!);

		Assert.IsFalse(store.Exists(directory!));
	}
}
