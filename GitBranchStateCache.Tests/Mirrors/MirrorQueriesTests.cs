// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Mirrors;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Tests.Fakes;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MirrorQueriesTests
{
	private const string Mirror = "/mirrors/github/studio/game";
	private const string Base = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string Tip = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
	private const string OtherTip = "cccccccccccccccccccccccccccccccccccccccc";
	private const string ForkPoint = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

	private static FakeGitRunner Answering(string mergeBase) => new()
	{
		Respond = _ => new GitResult(0, $"{mergeBase}\n", string.Empty, TimedOut: false),
	};

	private static MirrorQueries Build(IGitRunner runner, int maxCachedMergeBases = 100) =>
		new(runner, Options.Create(new GitBranchStateCacheOptions { MaxCachedMergeBases = maxCachedMergeBases }));

	[TestMethod]
	public async Task FindMergeBaseAsync_TheSameTwoCommits_RunsGitOnce()
	{
		// Every heartbeat from every editor asks for the same merge bases until someone pulls or a
		// branch moves. Both ids are immutable, so the second answer is the first one.
		FakeGitRunner runner = Answering(ForkPoint);
		MirrorQueries queries = Build(runner);

		await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);
		string? second = await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);

		Assert.AreEqual(ForkPoint, second);
		Assert.AreEqual(1, runner.CountOf("merge-base"));
	}

	[TestMethod]
	public async Task FindMergeBaseAsync_ADifferentTip_RunsGitAgain()
	{
		FakeGitRunner runner = Answering(ForkPoint);
		MirrorQueries queries = Build(runner);

		await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);
		await queries.FindMergeBaseAsync(Mirror, Base, OtherTip, CancellationToken.None);

		Assert.AreEqual(2, runner.CountOf("merge-base"));
	}

	[TestMethod]
	public async Task FindMergeBaseAsync_ADifferentMirror_RunsGitAgain()
	{
		FakeGitRunner runner = Answering(ForkPoint);
		MirrorQueries queries = Build(runner);

		await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);
		await queries.FindMergeBaseAsync("/mirrors/github/studio/other", Base, Tip, CancellationToken.None);

		Assert.AreEqual(2, runner.CountOf("merge-base"));
	}

	[TestMethod]
	public async Task FindMergeBaseAsync_AFailure_IsNotRemembered()
	{
		// A timeout says nothing about the commits, so the next request has to try again rather than
		// be told there is no merge base.
		FakeGitRunner runner = new()
		{
			Respond = _ => new GitResult(-1, string.Empty, "timed out", TimedOut: true),
		};
		MirrorQueries queries = Build(runner);

		string? first = await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);
		await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);

		Assert.IsNull(first);
		Assert.AreEqual(2, runner.CountOf("merge-base"));
	}

	[TestMethod]
	public async Task FindMergeBaseAsync_OverTheLimit_ForgetsTheLeastRecentlyUsed()
	{
		FakeGitRunner runner = Answering(ForkPoint);
		MirrorQueries queries = Build(runner, maxCachedMergeBases: 1);

		await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);
		await queries.FindMergeBaseAsync(Mirror, Base, OtherTip, CancellationToken.None);
		await queries.FindMergeBaseAsync(Mirror, Base, Tip, CancellationToken.None);

		Assert.AreEqual(3, runner.CountOf("merge-base"));
	}
}
