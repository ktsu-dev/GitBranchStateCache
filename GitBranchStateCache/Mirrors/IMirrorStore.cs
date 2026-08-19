// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

/// <summary>
/// Locates mirrors on disk and reports how current each one is.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about git. Creating and updating a mirror is
/// <see cref="IMirrorFetcher"/>'s job, which keeps everything about where a mirror lives, and whether
/// a request may be given one at all, testable against a filesystem that exists only in memory.
/// </remarks>
public interface IMirrorStore
{
	/// <summary>
	/// Resolves where a repository's mirror lives.
	/// </summary>
	/// <remarks>
	/// Refuses any repository path that cannot be turned into a directory path safely, rather than
	/// sanitizing it into something that might collide with another repository or escape the root.
	/// </remarks>
	/// <param name="key">The repository.</param>
	/// <param name="directory">The mirror directory, whether or not it exists yet.</param>
	/// <returns><see langword="true"/> when the repository path is one this service will mirror.</returns>
	public bool TryResolve(MirrorKey key, out string? directory);

	/// <summary>Reports whether a mirror has been created.</summary>
	/// <param name="directory">The mirror directory.</param>
	/// <returns><see langword="true"/> when it exists.</returns>
	public bool Exists(string directory);

	/// <summary>
	/// Reports when this mirror's refs were last known to match the upstream.
	/// </summary>
	/// <returns>The instant of the last successful fetch, or null when there has not been one.</returns>
	/// <param name="directory">The mirror directory.</param>
	public DateTimeOffset? RefsFetchedAt(string directory);

	/// <summary>Records that a fetch has just succeeded.</summary>
	/// <param name="directory">The mirror directory.</param>
	public void MarkFetched(string directory);

	/// <summary>Records that a request has just been answered from this mirror.</summary>
	/// <param name="directory">The mirror directory.</param>
	public void MarkUsed(string directory);

	/// <summary>Reports when this mirror last answered a request.</summary>
	/// <param name="directory">The mirror directory.</param>
	/// <returns>The instant, or null when it has never been recorded.</returns>
	public DateTimeOffset? LastUsedAt(string directory);

	/// <summary>Lists every mirror directory currently on disk.</summary>
	/// <returns>The directories.</returns>
	public IReadOnlyList<string> Enumerate();

	/// <summary>Removes a mirror and everything under it.</summary>
	/// <param name="directory">The mirror directory.</param>
	public void Delete(string directory);
}
