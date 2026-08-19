// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

/// <summary>
/// Identifies one computed diff.
/// </summary>
/// <param name="Repository">The repository, as upstream key and path.</param>
/// <param name="MergeBase">The commit the two sides last had in common.</param>
/// <param name="Tip">The commit the branch points at.</param>
/// <remarks>
/// Keyed on the merge base and not on the base the client sent. Keying on the client's base would
/// barely deduplicate anything, because every artist sits on a slightly different commit. The merge
/// base collapses them: a whole team working off one integration point shares one merge base, so one
/// computed diff per branch serves all of them.
/// <para>
/// Both halves are commit ids, which are immutable, so an entry can never be wrong. It can only be
/// cold, which means this cache needs eviction and never needs invalidation.
/// </para>
/// </remarks>
public sealed record DiffKey(string Repository, string MergeBase, string Tip);
