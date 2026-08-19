// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

/// <summary>
/// Produces the set of paths that differ between two commits.
/// </summary>
public interface IDiffSource
{
	/// <summary>
	/// Computes a diff between two commits of one mirror.
	/// </summary>
	/// <param name="directory">The mirror directory.</param>
	/// <param name="mergeBase">The commit the two sides last had in common.</param>
	/// <param name="tip">The commit the branch points at.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The paths that differ, or why they could not be obtained.</returns>
	/// <exception cref="DiffFormatException">git produced output that could not be read.</exception>
	public Task<DiffOutcome> ComputeAsync(
		string directory,
		string mergeBase,
		string tip,
		CancellationToken cancellationToken);
}
