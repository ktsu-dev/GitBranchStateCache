// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Refs;

using ktsu.GitBranchStateCache.Refs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class BranchPatternTests
{
	private static BranchPattern Parse(string pattern)
	{
		Assert.IsTrue(BranchPattern.TryParse(pattern, out BranchPattern? parsed, out _));
		return parsed!;
	}

	[TestMethod]
	public void Matches_ExactName_IsTrue() => Assert.IsTrue(Parse("origin/main").Matches("origin/main"));

	[TestMethod]
	public void Matches_DifferentName_IsFalse() => Assert.IsFalse(Parse("origin/main").Matches("origin/develop"));

	[TestMethod]
	public void Matches_WildcardCrossesSlashes()
	{
		// This is the whole reason the branch matcher is not the repository matcher. The plugin's
		// existing patterns were written for git branch --list, whose wildcard is not path-aware, so
		// origin/release/* has to keep matching origin/release/2026/q3 the way it does today.
		BranchPattern pattern = Parse("origin/release/*");

		Assert.IsTrue(pattern.Matches("origin/release/1.0"));
		Assert.IsTrue(pattern.Matches("origin/release/2026/q3"));
	}

	[TestMethod]
	public void Matches_TrailingWildcardOnly_MatchesTheHierarchy()
	{
		BranchPattern pattern = Parse("origin/*");

		Assert.IsTrue(pattern.Matches("origin/main"));
		Assert.IsTrue(pattern.Matches("origin/feature/ui/tweak"));
		Assert.IsFalse(pattern.Matches("upstream/main"));
	}

	[TestMethod]
	public void Matches_LeadingWildcard_MatchesAnyRemoteName() =>
		Assert.IsTrue(Parse("*/main").Matches("origin/main"));

	[TestMethod]
	public void Matches_MultipleWildcards_IsHandled()
	{
		BranchPattern pattern = Parse("origin/*/release/*");

		Assert.IsTrue(pattern.Matches("origin/game/release/1.0"));
		Assert.IsFalse(pattern.Matches("origin/game/main"));
	}

	[TestMethod]
	public void Matches_QuestionMark_MatchesOneCharacter()
	{
		BranchPattern pattern = Parse("origin/release/?.0");

		Assert.IsTrue(pattern.Matches("origin/release/1.0"));
		Assert.IsFalse(pattern.Matches("origin/release/10.0"));
	}

	[TestMethod]
	public void Matches_IsCaseSensitive() =>
		Assert.IsFalse(Parse("origin/Main").Matches("origin/main"));

	[TestMethod]
	public void Matches_RegularExpressionMetacharacters_AreLiteral()
	{
		// The pattern is caller-controlled, so a stray dot or bracket must be a character and not a
		// construct.
		Assert.IsFalse(Parse("origin/mai.").Matches("origin/main"));
		Assert.IsTrue(Parse("origin/v1.0").Matches("origin/v1.0"));
		Assert.IsFalse(Parse("origin/v1.0").Matches("origin/v1x0"));
	}

	[TestMethod]
	public void Matches_PathologicalPattern_StillReturnsPromptly()
	{
		// The shape that makes a naive regular expression translation catastrophic. This matcher
		// revisits only the most recent wildcard, so it cannot be driven exponential.
		BranchPattern pattern = Parse(new string('*', 40) + "z");

		Assert.IsFalse(pattern.Matches(new string('a', 4000)));
	}

	[TestMethod]
	public void TryParse_Empty_IsRefused() =>
		Assert.IsFalse(BranchPattern.TryParse(string.Empty, out _, out _));

	[TestMethod]
	public void TryParse_ExcessivelyLong_IsRefused() =>
		Assert.IsFalse(BranchPattern.TryParse(new string('a', 513), out _, out _));
}
