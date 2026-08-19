// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Admission;

using System.Diagnostics.Metrics;
using ktsu.GitBranchStateCache.Admission;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Observability;
using ktsu.GitBranchStateCache.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class AdmissionGateTests
{
	private const string Credential = "Basic dXNlcjp0b2tlbg==";
	private static readonly Uri UpstreamBase = new("https://github.com");
	private static readonly Uri RepositoryUrl = new("https://github.com/studio/game.git");

	private static (AdmissionGate Gate, FakeGitRunner Runner, CredentialAdmission Admission) Build()
	{
		GitBranchStateCacheOptions options = new();
		IOptions<GitBranchStateCacheOptions> wrapped = Options.Create(options);
		CredentialAdmission admission = new(wrapped, new FakeTimeProvider());
		FakeGitRunner runner = new();

		ServiceCollection services = new();
		services.AddMetrics();
		IMeterFactory meterFactory = services.BuildServiceProvider().GetRequiredService<IMeterFactory>();

		return (new AdmissionGate(runner, admission, new BranchStateMetrics(meterFactory), wrapped), runner, admission);
	}

	private static Task<AdmissionOutcome> AdmitAsync(AdmissionGate gate, string? credential = Credential) =>
		gate.AdmitAsync("github", "studio/game.git", RepositoryUrl, UpstreamBase, credential, CancellationToken.None);

	[TestMethod]
	public async Task AdmitAsync_WhenLsRemoteSucceeds_Admits()
	{
		(AdmissionGate gate, FakeGitRunner runner, _) = Build();

		AdmissionOutcome outcome = await AdmitAsync(gate);

		Assert.IsTrue(outcome.Admitted);
		Assert.AreEqual("ls-remote", runner.Invocations.Single().Arguments[0]);
	}

	[TestMethod]
	public async Task AdmitAsync_PassesTheCredentialThroughTheEnvironmentAndNeverAnArgument()
	{
		// A command line is world readable on Linux and this process handles many people's forge
		// credentials, so the credential must never appear in one.
		(AdmissionGate gate, FakeGitRunner runner, _) = Build();

		await AdmitAsync(gate);

		GitInvocation invocation = runner.Invocations.Single();
		Assert.AreEqual(Credential, invocation.Authorization);
		Assert.AreEqual(UpstreamBase, invocation.CredentialScope);
		Assert.IsFalse(invocation.Arguments.Any(argument => argument.Contains(Credential, StringComparison.Ordinal)));
	}

	[TestMethod]
	public async Task AdmitAsync_WhenLsRemoteIsRefused_DoesNotAdmit()
	{
		(AdmissionGate gate, FakeGitRunner runner, CredentialAdmission admission) = Build();
		runner.Respond = _ => new GitResult(128, string.Empty, "fatal: Authentication failed", TimedOut: false);

		AdmissionOutcome outcome = await AdmitAsync(gate);

		Assert.IsFalse(outcome.Admitted);
		Assert.AreEqual(401, outcome.StatusCode);
		Assert.IsFalse(admission.IsAdmitted("github", "studio/game.git", Credential));
	}

	[TestMethod]
	public async Task AdmitAsync_WhenTheRepositoryIsNotVisible_Is404()
	{
		// A forge deliberately conflates "no such repository" with "you may not see it", and relaying
		// that conflation unchanged keeps this service from telling a caller something the forge
		// declined to.
		(AdmissionGate gate, FakeGitRunner runner, _) = Build();
		runner.Respond = _ => new GitResult(128, string.Empty, "remote: Repository not found.", TimedOut: false);

		Assert.AreEqual(404, (await AdmitAsync(gate)).StatusCode);
	}

	[TestMethod]
	public async Task AdmitAsync_WhenTheForgeIsUnreachable_StillRefuses()
	{
		// Failing open on an outage would make every mirrored repository readable by anyone who could
		// reach this service for as long as the outage lasted.
		(AdmissionGate gate, FakeGitRunner runner, _) = Build();
		runner.Respond = _ => new GitResult(128, string.Empty, "fatal: unable to access: Could not resolve host", TimedOut: false);

		AdmissionOutcome outcome = await AdmitAsync(gate);

		Assert.IsFalse(outcome.Admitted);
		Assert.AreEqual(502, outcome.StatusCode);
	}

	[TestMethod]
	public async Task AdmitAsync_WhenTheProbeTimesOut_Refuses()
	{
		(AdmissionGate gate, FakeGitRunner runner, _) = Build();
		runner.Respond = _ => new GitResult(-1, string.Empty, "timeout", TimedOut: true);

		AdmissionOutcome outcome = await AdmitAsync(gate);

		Assert.IsFalse(outcome.Admitted);
		Assert.AreEqual(504, outcome.StatusCode);
	}

	[TestMethod]
	public async Task AdmitAsync_WithNoCredential_RefusesWithoutAskingTheUpstream()
	{
		(AdmissionGate gate, FakeGitRunner runner, _) = Build();

		AdmissionOutcome outcome = await AdmitAsync(gate, credential: null);

		Assert.IsFalse(outcome.Admitted);
		Assert.AreEqual(401, outcome.StatusCode);
		Assert.IsEmpty(runner.Invocations);
	}

	[TestMethod]
	public async Task AdmitAsync_Twice_ProbesTheUpstreamOnce()
	{
		// The reason admission exists at all: a studio on a thirty second heartbeat would otherwise
		// pay an ls-remote per editor per cycle.
		(AdmissionGate gate, FakeGitRunner runner, _) = Build();

		await AdmitAsync(gate);
		await AdmitAsync(gate);

		Assert.AreEqual(1, runner.CountOf("ls-remote"));
	}
}
