// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

/// <summary>
/// Creates a repository's mirror and keeps its refs current.
/// </summary>
/// <remarks>
/// Every upstream operation runs under a requesting client's own credential. A background poller on a
/// timer would be the obvious design and is deliberately not used, because it would need a credential
/// of its own and this service holds none.
/// </remarks>
public interface IMirrorFetcher
{
	/// <summary>
	/// Brings a mirror up to date, cloning it first if this is the first request for it.
	/// </summary>
	/// <param name="key">The repository.</param>
	/// <param name="directory">Where the mirror lives.</param>
	/// <param name="repositoryUrl">The upstream URL of the repository.</param>
	/// <param name="upstreamBase">The upstream base URL the credential is scoped to.</param>
	/// <param name="authorization">The requesting client's Authorization header.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>What state the mirror was left in.</returns>
	public Task<MirrorFetchResult> EnsureCurrentAsync(
		MirrorKey key,
		string directory,
		Uri repositoryUrl,
		Uri upstreamBase,
		string? authorization,
		CancellationToken cancellationToken);
}
