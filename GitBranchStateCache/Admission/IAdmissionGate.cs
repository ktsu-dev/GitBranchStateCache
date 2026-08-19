// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Admission;

/// <summary>
/// Proves a caller's credential against an upstream before anything is served.
/// </summary>
public interface IAdmissionGate
{
	/// <summary>
	/// Admits a caller, probing the upstream when there is no recent admission for them.
	/// </summary>
	/// <param name="upstream">The upstream key.</param>
	/// <param name="repositoryPath">The repository path.</param>
	/// <param name="repositoryUrl">The upstream URL of the repository.</param>
	/// <param name="upstreamBase">The upstream base URL the credential is scoped to.</param>
	/// <param name="authorization">The caller's Authorization header.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Whether the caller may be served.</returns>
	public Task<AdmissionOutcome> AdmitAsync(
		string upstream,
		string repositoryPath,
		Uri repositoryUrl,
		Uri upstreamBase,
		string? authorization,
		CancellationToken cancellationToken);
}
