// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Upstreams;

using ktsu.GitBranchStateCache.Upstreams;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RepositoryPatternTests
{
	[TestMethod]
	[DataRow("studio/game.git")]
	[DataRow("studio/tools-*")]
	[DataRow("myproject/_git/game")]
	[DataRow("studio/**")]
	public void TryParse_PatternNamingALiteralSegment_IsAccepted(string pattern) =>
		Assert.IsTrue(RepositoryPattern.TryParse(pattern, out _, out _));

	[TestMethod]
	[DataRow("**")]
	[DataRow("*")]
	[DataRow("*/*")]
	[DataRow("**/**")]
	[DataRow("*/**")]
	public void TryParse_PatternNamingNoLiteralSegment_IsRefused(string pattern)
	{
		// The difference from the object cache is the cost of being wrong. There, an unlisted
		// repository spends cache warmth. Here, one request for it is a permanent mirror clone onto a
		// shared volume, so there is deliberately no way to spell "everything".
		Assert.IsFalse(RepositoryPattern.TryParse(pattern, out _, out string? failure));
		Assert.Contains("literal", failure!);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public void TryParse_Empty_IsRefused(string pattern) =>
		Assert.IsFalse(RepositoryPattern.TryParse(pattern, out _, out _));

	[TestMethod]
	public void TryParse_EmptyInnerSegment_IsRefused() =>
		Assert.IsFalse(RepositoryPattern.TryParse("studio//game.git", out _, out _));

	[TestMethod]
	public void TryParse_Null_IsRefused() => Assert.IsFalse(RepositoryPattern.TryParse(null, out _, out _));

	[TestMethod]
	public void Matches_AnchorsBothEnds()
	{
		Assert.IsTrue(RepositoryPattern.TryParse("studio/game.git", out RepositoryPattern? pattern, out _));

		Assert.IsFalse(pattern!.Matches("other/studio/game.git"));
		Assert.IsFalse(pattern.Matches("studio/game.git.backup"));
	}

	[TestMethod]
	public void Matches_DotIsLiteral()
	{
		Assert.IsTrue(RepositoryPattern.TryParse("studio/game.git", out RepositoryPattern? pattern, out _));

		Assert.IsFalse(pattern!.Matches("studio/gameXgit"));
	}

	[TestMethod]
	[DataRow("studio/*-*-*", "studio", 1000)]
	[DataRow("studio/*-*", "studio", 4000)]
	[DataRow("team/*-svc-*", "team", 4000)]
	public void Matches_SeveralStarsInOneSegment_RejectsALongNonMatchingPathWithoutTimingOut(string text, string owner, int dashes)
	{
		// Two stars in one segment are adjacent [^/]* runs, which a backtracking engine takes
		// polynomial time to reject. The allow-list runs before admission, so an unauthenticated
		// caller could otherwise time the match out and get a 500 instead of a 404 (#111).
		Assert.IsTrue(RepositoryPattern.TryParse(text, out RepositoryPattern? pattern, out _));
		string path = $"{owner}/{new string('-', dashes)}/x";

		// Warm the expression up on a short path first, so the timing below measures the match rather
		// than the engine building its automaton.
		_ = pattern!.Matches($"{owner}/a/x");

		System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
		bool matched = pattern.Matches(path);
		stopwatch.Stop();

		Assert.IsFalse(matched);
		Assert.IsLessThan(50, stopwatch.ElapsedMilliseconds);
	}

	[TestMethod]
	public void Matches_SeveralStarsInOneSegment_StillMatchesWhatItShould()
	{
		Assert.IsTrue(RepositoryPattern.TryParse("team/*-svc-*", out RepositoryPattern? pattern, out _));

		Assert.IsTrue(pattern!.Matches("team/billing-svc-api"));
		Assert.IsTrue(pattern.Matches("Team/Billing-SVC-Api"));
		Assert.IsFalse(pattern.Matches("team/billing-api"));
		Assert.IsFalse(pattern.Matches("team/billing-svc-api/extra"));
	}
}
