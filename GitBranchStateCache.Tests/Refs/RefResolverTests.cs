// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Refs;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Refs;
using ktsu.GitBranchStateCache.Tests.Fakes;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RefResolverTests
{
	private const string MainTip = "1111111111111111111111111111111111111111";
	private const string ReleaseTip = "2222222222222222222222222222222222222222";

	private static IReadOnlyList<BranchPattern> Patterns(params string[] patterns) =>
		[.. patterns.Select(pattern =>
		{
			Assert.IsTrue(BranchPattern.TryParse(pattern, out BranchPattern? parsed, out _));
			return parsed!;
		})];

	[TestMethod]
	public void Parse_ReportsBranchesUnderTheRemoteName()
	{
		// The mirror keeps branches at refs/heads because that is where a bare mirror puts them. The
		// client thinks in terms of origin/main, and this is the one place the two are reconciled.
		string output = $"{MainTip} refs/heads/main\n{ReleaseTip} refs/heads/release/1.0\n";

		IReadOnlyList<BranchRef> branches = RefResolver.Parse(output, "origin");

		Assert.HasCount(2, branches);
		Assert.AreEqual("origin/main", branches[0].Name);
		Assert.AreEqual(MainTip, branches[0].Tip);
		Assert.AreEqual("origin/release/1.0", branches[1].Name);
	}

	[TestMethod]
	public void Parse_ToleratesCarriageReturns()
	{
		IReadOnlyList<BranchRef> branches = RefResolver.Parse($"{MainTip} refs/heads/main\r\n", "origin");

		Assert.AreEqual("origin/main", branches.Single().Name);
	}

	[TestMethod]
	public void Parse_IgnoresRefsOutsideHeads()
	{
		// Tags and notes come along with a clone and are not branches. Reporting them would give a
		// client names it can never match against its own remote-tracking branches.
		string output = $"{MainTip} refs/tags/v1.0\n{MainTip} refs/heads/main\n";

		Assert.AreEqual("origin/main", RefResolver.Parse(output, "origin").Single().Name);
	}

	[TestMethod]
	public void Parse_EmptyOutput_IsNoBranches() => Assert.IsEmpty(RefResolver.Parse(string.Empty, "origin"));

	[TestMethod]
	public void Match_HierarchyShapesThePluginUses()
	{
		BranchRef[] branches =
		[
			new("origin/main", MainTip),
			new("origin/release/1.0", ReleaseTip),
			new("origin/release/2.0", ReleaseTip),
			new("origin/feature/ui", MainTip),
		];

		IReadOnlyList<BranchRef> matched = RefResolver.Match(branches, Patterns("origin/main", "origin/release/*"));

		Assert.HasCount(3, matched);
		Assert.IsFalse(matched.Any(branch => branch.Name == "origin/feature/ui"));
	}

	[TestMethod]
	public void Match_PatternMatchingNothing_ReturnsNothingRatherThanEverything()
	{
		BranchRef[] branches = [new("origin/main", MainTip)];

		Assert.IsEmpty(RefResolver.Match(branches, Patterns("origin/nosuchthing/*")));
	}

	[TestMethod]
	public void Match_BranchCoveredByTwoPatterns_AppearsOnce()
	{
		// A client asking about its current branch and a wildcard that also covers it is the normal
		// case, not an edge one, and being told about the same branch twice would double every path
		// it carries.
		BranchRef[] branches = [new("origin/main", MainTip)];

		Assert.ContainsSingle(RefResolver.Match(branches, Patterns("origin/main", "origin/*")));
	}

	[TestMethod]
	public async Task ListAsync_WhenGitFails_ReportsNothingRatherThanNoBranches()
	{
		// An empty list and a failure mean opposite things to a client: one says nothing changed
		// anywhere, the other says this could not be answered.
		FakeGitRunner runner = new()
		{
			Respond = _ => new GitResult(128, string.Empty, "not a git repository", TimedOut: false),
		};

		RefResolver resolver = new(runner, Options.Create(new GitBranchStateCacheOptions()));

		Assert.IsNull(await resolver.ListAsync("/mirrors/x", CancellationToken.None));
	}

	[TestMethod]
	public async Task ListAsync_NamesOnlyRefsHeads()
	{
		FakeGitRunner runner = new()
		{
			Respond = _ => new GitResult(0, $"{MainTip} refs/heads/main\n", string.Empty, TimedOut: false),
		};

		RefResolver resolver = new(runner, Options.Create(new GitBranchStateCacheOptions()));
		await resolver.ListAsync("/mirrors/x", CancellationToken.None);

		Assert.Contains("refs/heads/", runner.Invocations.Single().Arguments);
	}
}
