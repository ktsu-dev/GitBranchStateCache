// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Tool;

using System.Text;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Tool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// A <c>--config</c> file layered over an <c>appsettings.json</c> in the working directory replaces each
/// upstream's allow-list it names, rather than patching it by index (#120).
/// </summary>
[TestClass]
public class ConfigFileAllowListTests
{
	private const string AppSettings = """
		{
		  "GitBranchStateCache": {
		    "Upstreams": {
		      "github": { "BaseUrl": "https://github.com", "Repositories": ["studio/sandbox.git", "studio/secret.git"] },
		      "ado": { "BaseUrl": "https://dev.azure.com/org", "Repositories": ["project/_git/game", "other/_git/tools"] }
		    }
		  }
		}
		""";

	private const string ConfigFile = """
		{
		  "GitBranchStateCache": {
		    "Upstreams": {
		      "GitHub": { "Repositories": ["studio/game.git"] }
		    }
		  }
		}
		""";

	/// <summary>
	/// Binds the options the way the tool does: the working directory's file, the <c>--config</c> file
	/// over it, then the flags over both.
	/// </summary>
	private static GitBranchStateCacheOptions Bind(string[] sources, params string[] allows)
	{
		Assert.IsTrue(
			Program.TryParseAllows(allows, out Dictionary<string, List<string>> allowLists, out string? invalid),
			invalid);

		ConfigurationBuilder builder = new();
		List<MemoryStream> streams = [];
		foreach (string source in sources)
		{
			MemoryStream stream = new(Encoding.UTF8.GetBytes(source));
			streams.Add(stream);
			builder.AddJsonStream(stream);
		}

		IConfigurationRoot configuration = builder.Build();
		foreach (MemoryStream stream in streams)
		{
			stream.Dispose();
		}

		Dictionary<string, List<string>> resolved = Program.ResolveAllowLists(configuration, allowLists);

		ServiceCollection services = new();
		services.AddOptions<GitBranchStateCacheOptions>()
			.Bind(configuration.GetSection(GitBranchStateCacheOptions.SectionName));
		services.PostConfigure<GitBranchStateCacheOptions>(options => Program.ReplaceAllowLists(options, resolved));

		using ServiceProvider provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IOptions<GitBranchStateCacheOptions>>().Value;
	}

	private static void AssertRepositories(GitBranchStateCacheOptions options, string upstream, params string[] expected) =>
		CollectionAssert.AreEqual(expected, options.Upstreams[upstream].Repositories.ToArray());

	[TestMethod]
	public void ConfigFile_ShorterList_ReplacesTheAppSettingsList()
	{
		GitBranchStateCacheOptions options = Bind([AppSettings, ConfigFile]);

		AssertRepositories(options, "github", "studio/game.git");
	}

	[TestMethod]
	public void ConfigFile_LeavesAnUpstreamItDoesNotNameAsAppSettingsHasIt()
	{
		GitBranchStateCacheOptions options = Bind([AppSettings, ConfigFile]);

		AssertRepositories(options, "ado", "project/_git/game", "other/_git/tools");
	}

	[TestMethod]
	public void ConfigFile_KeepsTheBaseUrlFromAppSettings()
	{
		GitBranchStateCacheOptions options = Bind([AppSettings, ConfigFile]);

		Assert.AreEqual("https://github.com", options.Upstreams["github"].BaseUrl?.ToString().TrimEnd('/'));
	}

	[TestMethod]
	public void Allow_BeatsBothFiles()
	{
		GitBranchStateCacheOptions options = Bind([AppSettings, ConfigFile], "github=studio/engine.git");

		AssertRepositories(options, "github", "studio/engine.git");
	}

	[TestMethod]
	public void SingleSource_KeepsItsListInIndexOrder()
	{
		const string TwelveEntries = """
			{ "GitBranchStateCache": { "Upstreams": { "github": { "Repositories": [
			  "a/0.git", "a/1.git", "a/2.git", "a/3.git", "a/4.git", "a/5.git",
			  "a/6.git", "a/7.git", "a/8.git", "a/9.git", "a/10.git", "a/11.git"
			] } } } }
			""";

		GitBranchStateCacheOptions options = Bind([TwelveEntries]);

		AssertRepositories(
			options,
			"github",
			"a/0.git", "a/1.git", "a/2.git", "a/3.git", "a/4.git", "a/5.git",
			"a/6.git", "a/7.git", "a/8.git", "a/9.git", "a/10.git", "a/11.git");
	}
}
