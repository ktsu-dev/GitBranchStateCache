// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Refs;

/// <summary>
/// One branch and the commit it points at.
/// </summary>
/// <param name="Name">
/// The branch as a client spells it, including the remote prefix, for example <c>origin/main</c>.
/// </param>
/// <param name="Tip">The commit id the branch points at.</param>
public sealed record BranchRef(string Name, string Tip);
