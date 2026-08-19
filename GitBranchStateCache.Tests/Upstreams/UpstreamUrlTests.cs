// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Upstreams;

using ktsu.GitBranchStateCache.Upstreams;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class UpstreamUrlTests
{
	private static readonly Uri GitHub = new("https://github.com");
	private static readonly Uri AzureDevOps = new("https://dev.azure.com/myorg/");

	[TestMethod]
	public void TryCombine_AppendsTheRepositoryPath()
	{
		Assert.IsTrue(UpstreamUrl.TryCombine(GitHub, "studio/game.git", out Uri? url));
		Assert.AreEqual("https://github.com/studio/game.git", url!.AbsoluteUri);
	}

	[TestMethod]
	public void TryCombine_BaseWithATrailingSlash_DoesNotDoubleIt()
	{
		Assert.IsTrue(UpstreamUrl.TryCombine(AzureDevOps, "myproject/_git/game", out Uri? url));
		Assert.AreEqual("https://dev.azure.com/myorg/myproject/_git/game", url!.AbsoluteUri);
	}

	[TestMethod]
	public void TryCombine_LeadingAndTrailingSlashes_AreIgnored()
	{
		Assert.IsTrue(UpstreamUrl.TryCombine(GitHub, "/studio/game.git/", out Uri? url));
		Assert.AreEqual("https://github.com/studio/game.git", url!.AbsoluteUri);
	}

	[TestMethod]
	[DataRow("studio/../../other")]
	[DataRow("../escape")]
	[DataRow("studio/./game")]
	[DataRow("studio\\game")]
	public void TryCombine_TraversalOrSeparatorTricks_AreRefused(string repositoryPath)
	{
		// Refused rather than normalized. Uri resolves .. silently, which would let a repository path
		// walk up out of the configured base and address a different part of the forge entirely.
		Assert.IsFalse(UpstreamUrl.TryCombine(GitHub, repositoryPath, out _));
	}

	[TestMethod]
	public void TryCombine_Empty_IsRefused() => Assert.IsFalse(UpstreamUrl.TryCombine(GitHub, string.Empty, out _));
}
