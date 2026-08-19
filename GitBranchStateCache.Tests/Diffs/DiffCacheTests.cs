// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Diffs;

using System.Diagnostics.Metrics;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Diffs;
using ktsu.GitBranchStateCache.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class DiffCacheTests
{
	private const string Repository = "github\nstudio/game.git";
	private const string MergeBase = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string Tip = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

	private sealed class CountingSource : IDiffSource
	{
		private int _calls;

		public int Calls => Volatile.Read(ref _calls);

		public Func<string, string, DiffOutcome> Respond { get; set; } =
			(mergeBase, tip) => DiffOutcome.Success([new DiffEntry("a.uasset", "1234", 'M')]);

		public Func<Task>? Before { get; set; }

		public async Task<DiffOutcome> ComputeAsync(
			string directory,
			string mergeBase,
			string tip,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _calls);

			if (Before is not null)
			{
				await Before().ConfigureAwait(false);
			}

			return Respond(mergeBase, tip);
		}
	}

	private static DiffCache Build(IDiffSource source, int maxCachedDiffs = 100)
	{
		ServiceCollection services = new();
		services.AddMetrics();
		IMeterFactory meterFactory = services.BuildServiceProvider().GetRequiredService<IMeterFactory>();

		return new DiffCache(
			source,
			new BranchStateMetrics(meterFactory),
			Options.Create(new GitBranchStateCacheOptions { MaxCachedDiffs = maxCachedDiffs }));
	}

	[TestMethod]
	public async Task GetAsync_TheSameMergeBaseAndTip_ComputesOnce()
	{
		// The reason the key is the merge base rather than the client's own base. Every artist sits on
		// a slightly different commit, but a whole team working off one integration point shares one
		// merge base, so one computation serves all of them.
		CountingSource source = new();
		DiffCache cache = Build(source);
		DiffKey key = new(Repository, MergeBase, Tip);

		await cache.GetAsync(key, "/mirror", CancellationToken.None);
		DiffOutcome second = await cache.GetAsync(key, "/mirror", CancellationToken.None);

		Assert.AreEqual(1, source.Calls);
		Assert.IsTrue(second.Succeeded);
		Assert.ContainsSingle(second.Entries!);
	}

	[TestMethod]
	public async Task GetAsync_ADifferentTip_IsComputedSeparately()
	{
		CountingSource source = new();
		DiffCache cache = Build(source);

		await cache.GetAsync(new DiffKey(Repository, MergeBase, Tip), "/mirror", CancellationToken.None);
		await cache.GetAsync(
			new DiffKey(Repository, MergeBase, "cccccccccccccccccccccccccccccccccccccccc"),
			"/mirror",
			CancellationToken.None);

		Assert.AreEqual(2, source.Calls);
	}

	[TestMethod]
	public async Task GetAsync_ADifferentRepository_IsComputedSeparately()
	{
		// Two repositories can genuinely share a merge base and a tip if one is a fork of the other,
		// so the repository has to be part of the key.
		CountingSource source = new();
		DiffCache cache = Build(source);

		await cache.GetAsync(new DiffKey(Repository, MergeBase, Tip), "/mirror", CancellationToken.None);
		await cache.GetAsync(new DiffKey("github\nstudio/fork.git", MergeBase, Tip), "/other", CancellationToken.None);

		Assert.AreEqual(2, source.Calls);
	}

	[TestMethod]
	public async Task GetAsync_AFailure_IsNotCached()
	{
		// A failure is a statement about this moment rather than about the commits, so caching it
		// would make a transient problem permanent for immutable keys that can never be invalidated.
		CountingSource source = new() { Respond = (_, _) => DiffOutcome.Failed("upstream was busy") };
		DiffCache cache = Build(source);
		DiffKey key = new(Repository, MergeBase, Tip);

		await cache.GetAsync(key, "/mirror", CancellationToken.None);
		await cache.GetAsync(key, "/mirror", CancellationToken.None);

		Assert.AreEqual(2, source.Calls);
	}

	[TestMethod]
	public async Task GetAsync_BeyondTheBound_EvictsTheLeastRecentlyUsed()
	{
		CountingSource source = new();
		DiffCache cache = Build(source, maxCachedDiffs: 2);

		DiffKey first = new(Repository, MergeBase, "1111111111111111111111111111111111111111");
		DiffKey second = new(Repository, MergeBase, "2222222222222222222222222222222222222222");
		DiffKey third = new(Repository, MergeBase, "3333333333333333333333333333333333333333");

		await cache.GetAsync(first, "/mirror", CancellationToken.None);
		await cache.GetAsync(second, "/mirror", CancellationToken.None);

		// Touching the first makes the second the least recently used.
		await cache.GetAsync(first, "/mirror", CancellationToken.None);
		await cache.GetAsync(third, "/mirror", CancellationToken.None);

		int before = source.Calls;
		await cache.GetAsync(first, "/mirror", CancellationToken.None);
		Assert.AreEqual(before, source.Calls);

		await cache.GetAsync(second, "/mirror", CancellationToken.None);
		Assert.AreEqual(before + 1, source.Calls);
	}

	[TestMethod]
	public async Task GetAsync_ConcurrentMissesForOneKey_ComputeOnce()
	{
		// The expected load is a whole studio polling on the same thirty second heartbeat, so the
		// first request after an integration lands would otherwise start the same expensive
		// computation once per editor.
		using SemaphoreSlim release = new(0);
		CountingSource source = new() { Before = () => release.WaitAsync() };
		DiffCache cache = Build(source);
		DiffKey key = new(Repository, MergeBase, Tip);

		Task<DiffOutcome>[] concurrent =
		[
			.. Enumerable.Range(0, 8).Select(_ => cache.GetAsync(key, "/mirror", CancellationToken.None))
		];

		release.Release(8);
		DiffOutcome[] outcomes = await Task.WhenAll(concurrent);

		Assert.AreEqual(1, source.Calls);
		Assert.IsTrue(outcomes.All(outcome => outcome.Succeeded));
	}
}
