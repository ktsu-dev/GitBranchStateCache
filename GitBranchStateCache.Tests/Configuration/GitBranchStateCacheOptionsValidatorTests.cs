// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Configuration;

using ktsu.GitBranchStateCache.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class GitBranchStateCacheOptionsValidatorTests
{
	private static readonly string Root = Path.Combine(
		Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
		"gitbranchstatecache");

	private static GitBranchStateCacheOptions Valid()
	{
		GitBranchStateCacheOptions options = new() { MirrorRoot = Root };
		UpstreamOptions upstream = new() { BaseUrl = new Uri("https://github.com") };
		upstream.Repositories.Add("studio/game.git");
		options.Upstreams["github"] = upstream;
		return options;
	}

	private static ValidateOptionsResult Validate(GitBranchStateCacheOptions options) =>
		new GitBranchStateCacheOptionsValidator().Validate(null, options);

	[TestMethod]
	public void Validate_AWorkableConfiguration_Succeeds() => Assert.IsTrue(Validate(Valid()).Succeeded);

	[TestMethod]
	public void Validate_NoUpstreams_Fails()
	{
		GitBranchStateCacheOptions options = new() { MirrorRoot = Root };

		Assert.IsTrue(Validate(options).Failed);
	}

	[TestMethod]
	public void Validate_UpstreamWithNoRepositories_Fails()
	{
		// Required with no default, because a deployment that mirrors whatever it is pointed at has to
		// be asked for rather than arrived at.
		GitBranchStateCacheOptions options = Valid();
		options.Upstreams["github"].Repositories.Clear();

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains("Repositories", result.FailureMessage);
	}

	[TestMethod]
	public void Validate_RepositoryPatternWithNoLiteralSegment_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.Upstreams["github"].Repositories[0] = "**";

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains("literal", result.FailureMessage);
	}

	[TestMethod]
	public void Validate_RelativeUpstreamUrl_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.Upstreams["github"].BaseUrl = new Uri("github.com", UriKind.Relative);

		Assert.IsTrue(Validate(options).Failed);
	}

	[TestMethod]
	public void Validate_MissingMirrorRoot_Fails()
	{
		// A cache whose volume is not mounted looks healthy from the outside and then fails every
		// request, so it has to fail at startup instead.
		GitBranchStateCacheOptions options = Valid();
		options.MirrorRoot = string.Empty;

		Assert.IsTrue(Validate(options).Failed);
	}

	[TestMethod]
	public void Validate_RelativeMirrorRoot_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.MirrorRoot = "mirrors";

		Assert.IsTrue(Validate(options).Failed);
	}

	[TestMethod]
	public void Validate_RefsTtlAboveAdmissionTtl_Fails()
	{
		// Branch state served from refs older than the authorization proving they may be read would
		// outlive the only evidence the caller was ever allowed to see it.
		GitBranchStateCacheOptions options = Valid();
		options.RefsTtl = TimeSpan.FromMinutes(5);
		options.AdmissionTtl = TimeSpan.FromMinutes(1);

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains("AdmissionTtl", result.FailureMessage);
	}

	[TestMethod]
	[DataRow("RefsTtl")]
	[DataRow("FetchTimeout")]
	[DataRow("DiffTimeout")]
	[DataRow("ProbeTimeout")]
	[DataRow("MaintenanceInterval")]
	public void Validate_NonPositiveTimeout_Fails(string setting)
	{
		GitBranchStateCacheOptions options = Valid();

		switch (setting)
		{
			case "RefsTtl":
				options.RefsTtl = TimeSpan.Zero;
				break;
			case "FetchTimeout":
				options.FetchTimeout = TimeSpan.Zero;
				break;
			case "DiffTimeout":
				options.DiffTimeout = TimeSpan.Zero;
				break;
			case "ProbeTimeout":
				options.ProbeTimeout = TimeSpan.Zero;
				break;
			default:
				options.MaintenanceInterval = TimeSpan.Zero;
				break;
		}

		Assert.IsTrue(Validate(options).Failed);
	}

	[TestMethod]
	public void Validate_ZeroIdleMaxAge_Succeeds()
	{
		// Zero is the documented way to keep every mirror indefinitely, so it is a choice rather than
		// a mistake.
		GitBranchStateCacheOptions options = Valid();
		options.MirrorIdleMaxAge = TimeSpan.Zero;

		Assert.IsTrue(Validate(options).Succeeded);
	}

	[TestMethod]
	public void Validate_NegativeIdleMaxAge_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.MirrorIdleMaxAge = TimeSpan.FromDays(-1);

		Assert.IsTrue(Validate(options).Failed);
	}

	[TestMethod]
	public void Validate_RemoteNameWithASlash_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.RemoteName = "origin/main";

		Assert.IsTrue(Validate(options).Failed);
	}

	[TestMethod]
	public void Validate_ReportsEveryProblemAtOnce()
	{
		// An operator fixing configuration by trial and error across restarts is a poor use of their
		// afternoon.
		GitBranchStateCacheOptions options = new() { MirrorRoot = string.Empty, MaxCachedDiffs = 0 };

		ValidateOptionsResult result = Validate(options);

		Assert.IsNotNull(result.Failures);
		Assert.IsTrue(result.Failures!.Count() >= 3);
	}
}
