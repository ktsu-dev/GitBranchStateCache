// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Endpoints;

using System.Text.Json.Serialization;

/// <summary>
/// What a client asks for.
/// </summary>
/// <remarks>
/// The base is the client's latest <em>pushed</em> ancestor, not its true HEAD, obtained locally with
/// no network access. Local commits are deliberately excluded: the mirror cannot compute a merge base
/// against a commit it has never seen, and an unpushed commit can only make the client more current
/// than the base it declares, which its own blob comparison catches anyway.
/// </remarks>
public sealed record StateRequest
{
	/// <summary>Gets the client's latest pushed ancestor, as a full object id.</summary>
	public string? Base { get; init; }

	/// <summary>
	/// Gets the branch patterns to answer for, in the same wildcard form the plugin already keeps in
	/// its status branch settings.
	/// </summary>
	public IReadOnlyList<string>? BranchPatterns { get; init; }

	/// <summary>
	/// Gets the repository-relative paths to answer about, or null for every changed path.
	/// </summary>
	/// <remarks>
	/// Omitting this is what a client warming its whole state wants, and is also how a request becomes
	/// enormous: a long-lived branch can differ in a hundred thousand paths. A client that knows which
	/// assets it cares about should say so.
	/// </remarks>
	public IReadOnlyList<string>? Paths { get; init; }
}

/// <summary>
/// One branch that was answered for.
/// </summary>
/// <param name="Name">The branch, as the client spells it.</param>
/// <param name="Tip">The commit the branch points at, read once at the start of the request.</param>
/// <param name="MergeBase">What the branch and the client's base last had in common.</param>
/// <param name="Error">Why this branch could not be answered for, when it could not.</param>
public sealed record BranchState(string Name, string Tip, string? MergeBase, string? Error);

/// <summary>
/// What one branch carries for one path.
/// </summary>
/// <param name="Branch">The branch.</param>
/// <param name="Blob">The blob id at that branch's tip, or null when the path was deleted there.</param>
/// <param name="Status">Git's raw status letter.</param>
public sealed record PathChange(string Branch, string? Blob, string Status);

/// <summary>
/// What the branch state endpoint answers.
/// </summary>
/// <param name="Base">The base the answer was computed against, echoed back.</param>
/// <param name="Branches">Every branch that matched, with its tip and merge base.</param>
/// <param name="Paths">
/// Every path that differs, keyed by path. A path absent from this is unchanged on every queried
/// branch relative to its merge base.
/// </param>
/// <param name="RefsAsOf">
/// When the refs this answer was computed from were last known to match the upstream. Older than the
/// request means a fetch did not succeed and the client is looking at slightly old data.
/// </param>
/// <param name="Partial">
/// Whether at least one branch could not be answered for. Labelled explicitly so a client can never
/// mistake a branch that failed for a branch with nothing changed on it.
/// </param>
/// <param name="Truncated">Whether the path limit stopped this response naming everything it found.</param>
public sealed record StateResponse(
	string Base,
	IReadOnlyList<BranchState> Branches,
	IReadOnlyDictionary<string, IReadOnlyList<PathChange>> Paths,
	DateTimeOffset? RefsAsOf,
	bool Partial,
	bool Truncated);

/// <summary>One branch and its tip.</summary>
/// <param name="Name">The branch, as the client spells it.</param>
/// <param name="Tip">The commit it points at.</param>
public sealed record BranchSummary(string Name, string Tip);

/// <summary>What the branch listing endpoint answers.</summary>
/// <param name="Branches">The matching branches.</param>
/// <param name="RefsAsOf">When the refs were last known to match the upstream.</param>
public sealed record BranchesResponse(IReadOnlyList<BranchSummary> Branches, DateTimeOffset? RefsAsOf);

/// <summary>What every refusal answers with.</summary>
/// <param name="Error">A stable machine-readable code.</param>
/// <param name="Message">What went wrong, for a human reading a log.</param>
/// <param name="Branches">
/// The current branch tips, sent with an unknown base so the client can decide what to do without a
/// second request.
/// </param>
public sealed record ErrorResponse(
	string Error,
	string Message,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<BranchSummary>? Branches = null);
