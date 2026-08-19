// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

/// <summary>
/// A bounded cache over <see cref="IDiffSource"/>.
/// </summary>
public interface IDiffCache
{
	/// <summary>
	/// Returns a diff, computing it only if it is not already held.
	/// </summary>
	/// <param name="key">What is being diffed.</param>
	/// <param name="directory">The mirror directory to compute in on a miss.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The paths that differ, or why they could not be obtained.</returns>
	public Task<DiffOutcome> GetAsync(DiffKey key, string directory, CancellationToken cancellationToken);
}
