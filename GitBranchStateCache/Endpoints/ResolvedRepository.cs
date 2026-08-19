// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Endpoints;

using ktsu.GitBranchStateCache.Mirrors;

/// <summary>
/// Everything a request needs about the repository it named, once it has been accepted.
/// </summary>
/// <param name="Key">The repository.</param>
/// <param name="Directory">Where its mirror lives.</param>
/// <param name="RepositoryUrl">Its upstream URL.</param>
/// <param name="UpstreamBase">The upstream base URL the credential is scoped to.</param>
internal sealed record ResolvedRepository(
	MirrorKey Key,
	string Directory,
	Uri RepositoryUrl,
	Uri UpstreamBase);
