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
	public void Validate_RefsServeStaleForPastAdmissionTtl_Succeeds()
	{
		// Capped where it is used rather than refused here, so a deployment whose RefsTtl already sits
		// close to AdmissionTtl keeps starting after an upgrade that introduced the default.
		GitBranchStateCacheOptions options = Valid();
		options.RefsTtl = TimeSpan.FromMinutes(1);
		options.AdmissionTtl = TimeSpan.FromMinutes(1);

		Assert.IsTrue(Validate(options).Succeeded);
	}

	[TestMethod]
	public void Validate_NegativeRefsServeStaleFor_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.RefsServeStaleFor = TimeSpan.FromSeconds(-1);

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains("RefsServeStaleFor", result.FailureMessage);
	}

	[TestMethod]
	public void Validate_ZeroRefsServeStaleFor_Succeeds()
	{
		// Zero is how a deployment makes every request past RefsTtl wait on its fetch.
		GitBranchStateCacheOptions options = Valid();
		options.RefsServeStaleFor = TimeSpan.Zero;

		Assert.IsTrue(Validate(options).Succeeded);
	}

	[TestMethod]
	public void Validate_NonPositiveMaxCachedMergeBases_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.MaxCachedMergeBases = 0;

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains("MaxCachedMergeBases", result.FailureMessage);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public void Validate_NonPositiveMaxBranchPatternsPerRequest_Fails(int limit)
	{
		GitBranchStateCacheOptions options = Valid();
		options.MaxBranchPatternsPerRequest = limit;

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains("MaxBranchPatternsPerRequest", result.FailureMessage);
	}

	[TestMethod]
	public void Validate_NonPositiveMaxConcurrentBranchQueries_Fails()
	{
		GitBranchStateCacheOptions options = Valid();
		options.MaxConcurrentBranchQueries = 0;

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains("MaxConcurrentBranchQueries", result.FailureMessage);
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

	private static void SetTimerSetting(GitBranchStateCacheOptions options, string setting, TimeSpan value)
	{
		switch (setting)
		{
			case "FetchTimeout":
				options.FetchTimeout = value;
				break;
			case "DiffTimeout":
				options.DiffTimeout = value;
				break;
			case "ProbeTimeout":
				options.ProbeTimeout = value;
				break;
			default:
				options.MaintenanceInterval = value;
				break;
		}
	}

	[TestMethod]
	[DataRow("FetchTimeout")]
	[DataRow("DiffTimeout")]
	[DataRow("ProbeTimeout")]
	[DataRow("MaintenanceInterval")]
	public void Validate_TimerSettingOf49Days_Succeeds(string setting)
	{
		GitBranchStateCacheOptions options = Valid();
		SetTimerSetting(options, setting, TimeSpan.FromDays(49));

		Assert.IsTrue(Validate(options).Succeeded);
	}

	[TestMethod]
	[DataRow("FetchTimeout")]
	[DataRow("DiffTimeout")]
	[DataRow("ProbeTimeout")]
	[DataRow("MaintenanceInterval")]
	public void Validate_TimerSettingOf50Days_Fails(string setting)
	{
		// Past uint.MaxValue - 1 ms, CancellationTokenSource, Task.WaitAsync and PeriodicTimer throw, so a
		// value meant as "effectively no timeout" has to be refused here, naming the setting.
		GitBranchStateCacheOptions options = Valid();
		SetTimerSetting(options, setting, TimeSpan.FromDays(50));

		ValidateOptionsResult result = Validate(options);

		Assert.IsTrue(result.Failed);
		Assert.Contains(setting, result.FailureMessage);
		Assert.Contains(GitBranchStateCacheOptionsValidator.MaxTimerDuration.ToString(), result.FailureMessage);
	}

	[TestMethod]
	public void MaxTimerDuration_IsTheLongestDurationTheTimersAccept()
	{
		TimeSpan max = GitBranchStateCacheOptionsValidator.MaxTimerDuration;

		using CancellationTokenSource accepted = new(max);
		using PeriodicTimer timer = new(max);
		TimeSpan tooLong = max + TimeSpan.FromMilliseconds(1);
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CancellationTokenSource(tooLong));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PeriodicTimer(tooLong));
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
