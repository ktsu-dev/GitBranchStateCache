// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Tool;

using System.Text;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Tool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

[TestClass]
public class AllowFlagTests
{
	private const string ConfigFile = """
		{
		  "GitBranchStateCache": {
		    "Upstreams": {
		      "github": { "BaseUrl": "https://github.com", "Repositories": ["studio/game.git", "studio/tools.git"] },
		      "ado": { "BaseUrl": "https://dev.azure.com/org", "Repositories": ["project/_git/game", "other/_git/tools"] }
		    }
		  }
		}
		""";

	/// <summary>
	/// Binds the options the way the tool does: a configuration file, then the flags over it.
	/// </summary>
	private static GitBranchStateCacheOptions Bind(params string[] allows)
	{
		Assert.IsTrue(
			Program.TryParseAllows(allows, out Dictionary<string, List<string>> allowLists, out string? invalid),
			invalid);

		using MemoryStream file = new(Encoding.UTF8.GetBytes(ConfigFile));
		IConfiguration configuration = new ConfigurationBuilder().AddJsonStream(file).Build();

		ServiceCollection services = new();
		services.AddOptions<GitBranchStateCacheOptions>()
			.Bind(configuration.GetSection(GitBranchStateCacheOptions.SectionName));
		services.PostConfigure<GitBranchStateCacheOptions>(options => Program.ReplaceAllowLists(options, allowLists));

		using ServiceProvider provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IOptions<GitBranchStateCacheOptions>>().Value;
	}

	private static void AssertRepositories(GitBranchStateCacheOptions options, string upstream, params string[] expected) =>
		CollectionAssert.AreEqual(expected, options.Upstreams[upstream].Repositories.ToArray());

	[TestMethod]
	public void Allow_FewerPatternsThanConfigured_ReplacesTheConfiguredList()
	{
		GitBranchStateCacheOptions options = Bind("github=studio/engine.git");

		AssertRepositories(options, "github", "studio/engine.git");
	}

	[TestMethod]
	public void Allow_AsManyPatternsAsConfigured_ReplacesTheConfiguredList()
	{
		GitBranchStateCacheOptions options = Bind("github=a/one.git", "github=b/two.git");

		AssertRepositories(options, "github", "a/one.git", "b/two.git");
	}

	[TestMethod]
	public void Allow_Repeated_KeepsEveryPatternInOrder()
	{
		GitBranchStateCacheOptions options = Bind("github=a/one.git", "GitHub=b/two.git", "github=c/three.git");

		AssertRepositories(options, "github", "a/one.git", "b/two.git", "c/three.git");
	}

	[TestMethod]
	public void Allow_ForOneUpstream_LeavesTheOthersConfiguredList()
	{
		GitBranchStateCacheOptions options = Bind("github=studio/engine.git");

		AssertRepositories(options, "ado", "project/_git/game", "other/_git/tools");
	}

	[TestMethod]
	public void Allow_ForAnUnconfiguredUpstream_AddsItWithThoseRepositories()
	{
		GitBranchStateCacheOptions options = Bind("gitlab=team/app.git");

		AssertRepositories(options, "gitlab", "team/app.git");
	}

	[TestMethod]
	public void NoAllow_KeepsTheConfiguredList()
	{
		GitBranchStateCacheOptions options = Bind();

		AssertRepositories(options, "github", "studio/game.git", "studio/tools.git");
	}

	[TestMethod]
	[DataRow("github")]
	[DataRow("=studio/game.git")]
	[DataRow("github=")]
	public void TryParseAllows_Malformed_ReportsTheEntry(string entry)
	{
		Assert.IsFalse(Program.TryParseAllows([entry], out _, out string? invalid));
		Assert.AreEqual(entry, invalid);
	}
}
