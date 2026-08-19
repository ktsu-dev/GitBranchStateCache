// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

/// <summary>
/// Asks a mirror the two questions a state request needs answered before it can diff anything.
/// </summary>
public interface IMirrorQueries
{
	/// <summary>
	/// Reports whether the mirror holds a commit.
	/// </summary>
	/// <remarks>
	/// Asked before anything else uses the client's base, so a client that has not pushed in a long
	/// time, or that has rewritten history, gets a clear answer rather than an unexplained failure
	/// from whatever would have used the commit next.
	/// </remarks>
	/// <param name="directory">The mirror directory.</param>
	/// <param name="commit">A full object id.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns><see langword="true"/> when the mirror has it.</returns>
	public Task<bool> ContainsCommitAsync(string directory, string commit, CancellationToken cancellationToken);

	/// <summary>
	/// Finds the commit two commits last had in common.
	/// </summary>
	/// <param name="directory">The mirror directory.</param>
	/// <param name="first">A full object id.</param>
	/// <param name="second">A full object id.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The merge base, or null when there is none or it could not be found.</returns>
	public Task<string?> FindMergeBaseAsync(
		string directory,
		string first,
		string second,
		CancellationToken cancellationToken);
}
