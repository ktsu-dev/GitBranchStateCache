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

	/// <summary>Records that a request is using this mirror.</summary>
	/// <remarks>
	/// Recorded when a request starts working against the mirror as well as when one has been answered
	/// from it, so that a long fetch does not look idle to anything reading this marker while it runs.
	/// </remarks>
	/// <param name="directory">The mirror directory.</param>
	public void MarkUsed(string directory);

	/// <summary>Reports when a request last used this mirror.</summary>
	/// <param name="directory">The mirror directory.</param>
	/// <returns>The instant, or null when it has never been recorded.</returns>
	public DateTimeOffset? LastUsedAt(string directory);

	/// <summary>Lists every mirror directory currently on disk.</summary>
	/// <returns>The directories.</returns>
	public IReadOnlyList<string> Enumerate();

	/// <summary>
	/// Lists every directory a clone is staging into, or was when it died.
	/// </summary>
	/// <remarks>
	/// A clone is written beside its mirror under a name only a clone uses, and moved into place when it
	/// finishes. One whose process was killed first is never moved or removed by that clone, so it is
	/// left for the maintenance sweep, which is the only thing that looks for it.
	/// </remarks>
	/// <returns>The directories.</returns>
	public IReadOnlyList<string> EnumerateStaging();

	/// <summary>Removes a mirror and everything under it.</summary>
	/// <param name="directory">The mirror directory.</param>
	public void Delete(string directory);
}
