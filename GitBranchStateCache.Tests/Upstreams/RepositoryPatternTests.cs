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
}
