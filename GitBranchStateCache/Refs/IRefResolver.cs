// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Refs;

/// <summary>
/// Reads a mirror's branches and expands client patterns against them.
/// </summary>
public interface IRefResolver
{
	/// <summary>
	/// Lists every branch the mirror holds, with the commit each points at.
	/// </summary>
	/// <remarks>
	/// The tip ids are read once, here, and every later step of a request names those ids explicitly
	/// rather than the branch they came from. That is what stops a fetch landing mid-request from
	/// producing an answer torn between two versions of a branch.
	/// </remarks>
	/// <param name="directory">The mirror directory.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The branches, or null when the mirror could not be read.</returns>
	public Task<IReadOnlyList<BranchRef>?> ListAsync(string directory, CancellationToken cancellationToken);
}
