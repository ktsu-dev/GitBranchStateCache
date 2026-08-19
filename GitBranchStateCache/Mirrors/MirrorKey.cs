// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

/// <summary>
/// Identifies one mirrored repository.
/// </summary>
/// <param name="Upstream">The upstream key the repository is addressed under.</param>
/// <param name="RepositoryPath">The repository path following the upstream key.</param>
public sealed record MirrorKey(string Upstream, string RepositoryPath)
{
	/// <summary>
	/// Renders a key usable for coalescing work on this repository.
	/// </summary>
	/// <remarks>
	/// The separator cannot appear in an upstream key, so no two different pairs can produce the same
	/// string.
	/// </remarks>
	/// <returns>The coalescing key.</returns>
	public string ToFlightKey() => $"{Upstream}\n{RepositoryPath}";
}
