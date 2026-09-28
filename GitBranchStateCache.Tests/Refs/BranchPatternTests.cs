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
	public void Matches_BracketRange_MatchesTheWayGitBranchListDoes()
	{
		// The shape a StatusBranchNamePatterns entry takes in the field. git branch --list honours the
		// bracket expression, so the service has to, or those branches silently drop out of /state.
		BranchPattern pattern = Parse("origin/release-[0-9]*");

		Assert.IsTrue(pattern.Matches("origin/release-1.0"));
		Assert.IsTrue(pattern.Matches("origin/release-2026"));
		Assert.IsFalse(pattern.Matches("origin/release-x"));
	}

	[TestMethod]
	public void Matches_BracketSet_MatchesAnyMember()
	{
		BranchPattern pattern = Parse("origin/[abc]");

		Assert.IsTrue(pattern.Matches("origin/a"));
		Assert.IsTrue(pattern.Matches("origin/c"));
		Assert.IsFalse(pattern.Matches("origin/d"));
		Assert.IsFalse(pattern.Matches("origin/ab"));
	}

	[TestMethod]
	public void Matches_NegatedBracket_AcceptsBangAndCaret()
	{
		foreach (string text in new[] { "origin/[!x]y", "origin/[^x]y" })
		{
			BranchPattern pattern = Parse(text);

			Assert.IsTrue(pattern.Matches("origin/ay"), text);
			Assert.IsFalse(pattern.Matches("origin/xy"), text);
		}
	}

	[TestMethod]
	public void Matches_CloseBracketFirst_IsAMember()
	{
		Assert.IsTrue(Parse("a[]b]c").Matches("a]c"));
		Assert.IsTrue(Parse("a[]b]c").Matches("abc"));
		Assert.IsFalse(Parse("a[!]]c").Matches("a]c"));
		Assert.IsTrue(Parse("a[!]]c").Matches("abc"));
	}

	[TestMethod]
	public void Matches_DashAtEitherEnd_IsAMember()
	{
		Assert.IsTrue(Parse("v[-.]1").Matches("v-1"));
		Assert.IsTrue(Parse("v[.-]1").Matches("v-1"));
		Assert.IsFalse(Parse("v[.-]1").Matches("v/1"));
	}

	[TestMethod]
	public void Matches_Backslash_EscapesTheNextCharacter()
	{
		Assert.IsTrue(Parse(@"origin/a\*b").Matches("origin/a*b"));
		Assert.IsFalse(Parse(@"origin/a\*b").Matches("origin/axyb"));
		Assert.IsTrue(Parse(@"origin/\[x]").Matches("origin/[x]"));
		Assert.IsFalse(Parse(@"origin/\[x]").Matches("origin/x"));
		Assert.IsTrue(Parse(@"origin/[\]]").Matches("origin/]"));
	}

	[TestMethod]
	public void Matches_UnclosedBracket_IsLiteral()
	{
		Assert.IsTrue(Parse("origin/[main").Matches("origin/[main"));
		Assert.IsFalse(Parse("origin/[main").Matches("origin/main"));
	}

	[TestMethod]
	public void Matches_BracketAfterWildcard_BacktracksCorrectly() =>
		Assert.IsTrue(Parse("*/v[0-9].[0-9]").Matches("origin/feature/v1.2"));

	[TestMethod]
	public void Matches_PathologicalBracketPattern_StillReturnsPromptly()
	{
		BranchPattern pattern = Parse(string.Concat(Enumerable.Repeat("*[a-y]", 40)) + "z");

		Assert.IsFalse(pattern.Matches(new string('a', 4000)));
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
