// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Git;

/// <summary>
/// Recognises a full git object id.
/// </summary>
/// <remarks>
/// Every commit a client names is checked against this before it reaches a git command line. Git
/// accepts a great deal more than object ids where one is expected: <c>HEAD</c>, <c>main@{1}</c>,
/// <c>:/some message</c>, and anything beginning with a dash that a command reads as an option. None
/// of those are things a client has any business asking about, and refusing them here means the
/// arguments this service builds are only ever ids it has already recognised.
/// </remarks>
public static class ObjectId
{
	/// <summary>
	/// Reports whether a string is a full object id.
	/// </summary>
	/// <remarks>
	/// Full length only, in the two lengths git produces for SHA-1 and SHA-256 repositories.
	/// Abbreviations are refused because they can be ambiguous, and an ambiguous id resolving to the
	/// wrong commit would produce a confidently wrong answer.
	/// </remarks>
	/// <param name="candidate">The string to check.</param>
	/// <returns><see langword="true"/> when it is a full object id.</returns>
	public static bool IsValid(string? candidate) =>
		candidate is not null
		&& candidate.Length is 40 or 64
		&& candidate.All(char.IsAsciiHexDigitLower);
}
