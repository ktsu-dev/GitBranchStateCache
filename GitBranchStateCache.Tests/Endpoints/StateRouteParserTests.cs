// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Endpoints;

using ktsu.GitBranchStateCache.Endpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class StateRouteParserTests
{
	[TestMethod]
	public void Parse_StateRoute_SplitsTheRepositoryPath()
	{
		StateRoute route = StateRouteParser.Parse("/v1/github/studio/game.git/state");

		Assert.AreEqual(StateRouteKind.State, route.Kind);
		Assert.AreEqual("github", route.Upstream);
		Assert.AreEqual("studio/game.git", route.RepositoryPath);
	}

	[TestMethod]
	public void Parse_BranchesRoute_IsRecognised()
	{
		StateRoute route = StateRouteParser.Parse("/v1/ado/myproject/_git/game/branches");

		Assert.AreEqual(StateRouteKind.Branches, route.Kind);
		Assert.AreEqual("ado", route.Upstream);
		Assert.AreEqual("myproject/_git/game", route.RepositoryPath);
	}

	[TestMethod]
	public void Parse_DeeplyNestedRepositoryPath_IsKeptWhole()
	{
		// The reason this is parsed rather than routed: a repository path has variable depth and the
		// endpoint name is what has to come last.
		StateRoute route = StateRouteParser.Parse("/v1/github/a/b/c/d/e/state");

		Assert.AreEqual("a/b/c/d/e", route.RepositoryPath);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("/")]
	[DataRow("/healthz")]
	[DataRow("/v1/github/state")]
	[DataRow("/v2/github/studio/game.git/state")]
	[DataRow("/v1/github/studio/game.git")]
	[DataRow("/v1/github/studio/game.git/locks")]
	public void Parse_AnythingElse_IsUnknown(string path) =>
		Assert.AreEqual(StateRouteKind.Unknown, StateRouteParser.Parse(path).Kind);

	[TestMethod]
	public void Parse_Null_IsUnknown() =>
		Assert.AreEqual(StateRouteKind.Unknown, StateRouteParser.Parse(null).Kind);

	[TestMethod]
	public void Parse_RepeatedSlashes_AreIgnored() =>
		Assert.AreEqual("studio/game.git", StateRouteParser.Parse("//v1//github//studio//game.git//state").RepositoryPath);
}
