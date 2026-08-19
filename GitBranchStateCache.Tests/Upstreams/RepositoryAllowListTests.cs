// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Upstreams;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Upstreams;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RepositoryAllowListTests
{
	private static RepositoryAllowList Build(params string[] patterns)
	{
		GitBranchStateCacheOptions options = new();
		UpstreamOptions upstream = new() { BaseUrl = new Uri("https://github.com") };

		foreach (string pattern in patterns)
		{
			upstream.Repositories.Add(pattern);
		}

		options.Upstreams["github"] = upstream;
		return new RepositoryAllowList(Options.Create(options));
	}

	[TestMethod]
	public void IsAllowed_ExactRepository_IsTrue()
	{
		// Unlike the Git LFS proxy, a pattern here names the repository and nothing follows it, so a
		// complete pattern needs no trailing wildcard.
		RepositoryAllowList allowList = Build("studio/game.git");

		Assert.IsTrue(allowList.IsAllowed("github", "studio/game.git"));
	}

	[TestMethod]
	public void IsAllowed_DifferentRepository_IsFalse() =>
		Assert.IsFalse(Build("studio/game.git").IsAllowed("github", "studio/other.git"));

	[TestMethod]
	public void IsAllowed_WildcardWithinASegment_IsHonoured()
	{
		RepositoryAllowList allowList = Build("studio/tools-*");

		Assert.IsTrue(allowList.IsAllowed("github", "studio/tools-build"));
		Assert.IsFalse(allowList.IsAllowed("github", "studio/game"));
	}

	[TestMethod]
	public void IsAllowed_SingleWildcardDoesNotCrossSegments() =>
		Assert.IsFalse(Build("studio/*").IsAllowed("github", "studio/nested/repo.git"));

	[TestMethod]
	public void IsAllowed_DoubleWildcardCrossesSegments() =>
		Assert.IsTrue(Build("studio/**").IsAllowed("github", "studio/nested/repo.git"));

	[TestMethod]
	public void IsAllowed_IsCaseInsensitive() =>
		Assert.IsTrue(Build("studio/game.git").IsAllowed("github", "Studio/Game.git"));

	[TestMethod]
	public void IsAllowed_UnknownUpstream_IsFalse() =>
		Assert.IsFalse(Build("studio/game.git").IsAllowed("ado", "studio/game.git"));

	[TestMethod]
	public void IsAllowed_TraversalAttempt_IsFalse() =>
		Assert.IsFalse(Build("studio/game.git").IsAllowed("github", "studio/../other/game.git"));

	[TestMethod]
	public void IsAllowed_NoPatterns_IsFalse()
	{
		// Startup validation refuses this configuration outright, so this only asserts that the
		// fallback is refusal rather than acceptance.
		Assert.IsFalse(Build().IsAllowed("github", "studio/game.git"));
	}

	[TestMethod]
	public void IsAllowed_PatternWithNoLiteralSegment_IsDropped()
	{
		// The validator refuses this at startup. If one ever reaches this far, it must not quietly
		// become permission to clone anything anyone asks for.
		Assert.IsFalse(Build("**").IsAllowed("github", "anyone/anything.git"));
	}
}
