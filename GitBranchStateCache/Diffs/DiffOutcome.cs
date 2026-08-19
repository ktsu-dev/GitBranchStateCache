// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

/// <summary>
/// What happened when a diff was asked for.
/// </summary>
/// <param name="Entries">The paths that differ, when the diff was produced.</param>
/// <param name="Failure">Why it was not produced, when it was not.</param>
/// <param name="TimedOut">Whether the failure was the diff exceeding its budget.</param>
/// <remarks>
/// A failure here is per branch and never per request. One long-lived branch that takes too long to
/// diff must not stop a client learning about the other three, so the branch is reported as failed
/// and the response is labelled as partial. What is never done is report the branch as having nothing
/// changed on it, which is the one wrong answer with real consequences.
/// </remarks>
public sealed record DiffOutcome(IReadOnlyList<DiffEntry>? Entries, string? Failure, bool TimedOut)
{
	/// <summary>Gets a value indicating whether the diff was produced.</summary>
	public bool Succeeded => Entries is not null;

	/// <summary>Reports a diff that was produced.</summary>
	/// <param name="entries">The paths that differ.</param>
	/// <returns>The outcome.</returns>
	public static DiffOutcome Success(IReadOnlyList<DiffEntry> entries) => new(entries, null, false);

	/// <summary>Reports a diff that could not be produced.</summary>
	/// <param name="failure">Why not.</param>
	/// <returns>The outcome.</returns>
	public static DiffOutcome Failed(string failure) => new(null, failure, false);

	/// <summary>Reports a diff that ran out of time.</summary>
	/// <returns>The outcome.</returns>
	public static DiffOutcome Timeout() =>
		new(null, "The diff for this branch exceeded its time budget.", true);
}
