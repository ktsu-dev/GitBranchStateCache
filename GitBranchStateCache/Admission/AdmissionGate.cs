// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Admission;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

/// <summary>
/// Proves a credential with <c>git ls-remote</c>.
/// </summary>
/// <remarks>
/// <c>ls-remote</c> is the whole authorization mechanism, and that is a deliberate choice rather than
/// a convenience. It speaks the same protocol against GitHub and Azure DevOps, so there is no
/// per-forge adapter to write or keep correct; it is one cheap round trip; and it proves precisely
/// the access being requested, which is read access to this repository by this credential. A forge
/// REST call would prove the same thing while reintroducing the per-forge layer the rest of this
/// design exists to avoid.
/// <para>
/// There is no path through this that admits without a successful upstream call. A forge that cannot
/// be reached produces a refusal, not an exception to the rule.
/// </para>
/// </remarks>
/// <param name="runner">Runs git.</param>
/// <param name="admission">Remembers which credentials the upstream accepted.</param>
/// <param name="metrics">Service counters.</param>
/// <param name="options">The configured options.</param>
public sealed class AdmissionGate(
	IGitRunner runner,
	ICredentialAdmission admission,
	BranchStateMetrics metrics,
	IOptions<GitBranchStateCacheOptions> options) : IAdmissionGate
{
	/// <summary>
	/// Fragments of git's failure output that mean the credential was the problem.
	/// </summary>
	/// <remarks>
	/// Matched to choose a status code and nothing else. Every branch of this refuses; the only
	/// question is whether the caller is told to fix their credential or that the forge could not be
	/// reached, and getting that wrong costs a confusing message rather than an incorrect decision.
	/// </remarks>
	private static readonly string[] AuthenticationMarkers =
	[
		"authentication failed",
		"could not read username",
		"could not read password",
		"invalid username or password",
		"http basic: access denied",
		"403",
		"401",
	];

	/// <summary>
	/// Fragments that mean the upstream will not admit this repository exists to this caller.
	/// </summary>
	/// <remarks>
	/// A forge deliberately conflates "no such repository" with "you may not see this repository", and
	/// that conflation is worth preserving rather than resolving. Relaying it unchanged keeps this
	/// service from telling a caller something the forge itself declined to.
	/// </remarks>
	private static readonly string[] NotFoundMarkers =
	[
		"repository not found",
		"does not exist",
		"not found",
	];

	/// <inheritdoc />
	public async Task<AdmissionOutcome> AdmitAsync(
		string upstream,
		string repositoryPath,
		Uri repositoryUrl,
		Uri upstreamBase,
		string? authorization,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(repositoryUrl);

		if (string.IsNullOrEmpty(authorization))
		{
			return AdmissionOutcome.Deny(
				StatusCodes.Status401Unauthorized,
				"This service serves nothing without a credential it can prove against the upstream.");
		}

		if (admission.IsAdmitted(upstream, repositoryPath, authorization))
		{
			return AdmissionOutcome.Allow();
		}

		metrics.RecordAdmissionProbe(upstream);

		GitResult probe = await runner.RunAsync(
			new GitInvocation
			{
				// No working directory: this asks the upstream a question and touches no mirror. That
				// matters, because it runs before this service has decided to create one.
				Arguments = ["ls-remote", "--heads", repositoryUrl.AbsoluteUri],
				CredentialScope = upstreamBase,
				Authorization = authorization,
				Timeout = options.Value.ProbeTimeout,
			},
			cancellationToken).ConfigureAwait(false);

		if (probe.Succeeded)
		{
			admission.Admit(upstream, repositoryPath, authorization);
			return AdmissionOutcome.Allow();
		}

		metrics.RecordAdmissionRejected(upstream);
		return Classify(probe);
	}

	private static AdmissionOutcome Classify(GitResult probe)
	{
		if (probe.TimedOut)
		{
			return AdmissionOutcome.Deny(
				StatusCodes.Status504GatewayTimeout,
				"The upstream did not answer in time, so this request cannot be authorized.");
		}

		string output = probe.Summary.ToLowerInvariant();

		if (AuthenticationMarkers.Any(marker => output.Contains(marker, StringComparison.Ordinal)))
		{
			return AdmissionOutcome.Deny(
				StatusCodes.Status401Unauthorized,
				"The upstream refused this credential.");
		}

		if (NotFoundMarkers.Any(marker => output.Contains(marker, StringComparison.Ordinal)))
		{
			return AdmissionOutcome.Deny(
				StatusCodes.Status404NotFound,
				"The upstream does not have this repository, or will not show it to this credential.");
		}

		return AdmissionOutcome.Deny(
			StatusCodes.Status502BadGateway,
			$"The upstream could not be asked whether this credential may read this repository: {probe.Summary}");
	}
}
