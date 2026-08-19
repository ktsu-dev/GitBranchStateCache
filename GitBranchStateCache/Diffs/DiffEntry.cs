// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

/// <summary>
/// One path that differs between two commits.
/// </summary>
/// <param name="Path">The path, relative to the repository root, as git reports it.</param>
/// <param name="Blob">
/// The blob id the path has at the second commit, or null when it does not exist there.
/// </param>
/// <param name="Status">
/// Git's raw status letter, so a client can tell a delete from a modification.
/// </param>
/// <remarks>
/// A blob id rather than content, and rather than a verdict. The client compares it against its own
/// blob id for that path, which is local, exact, and cheap. That comparison is strictly better than
/// intersecting a log with a diff, which is only an approximation of the case where a file was changed
/// and then changed back; two identical files always have the same blob id, so comparing ids catches
/// that case and every other one exactly.
/// <para>
/// It is also exactly right for an asset tracked by Git LFS, with no special handling. Such a file is
/// stored in git as a small pointer naming the LFS object id, so comparing pointer blob ids is
/// comparing LFS object ids, and this service never has to know which files are LFS-tracked.
/// </para>
/// </remarks>
public sealed record DiffEntry(string Path, string? Blob, char Status);
