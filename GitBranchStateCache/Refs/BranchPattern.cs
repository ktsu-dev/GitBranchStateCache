// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Refs;

/// <summary>
/// A branch name pattern, matched the way git matches one.
/// </summary>
/// <remarks>
/// The wildcard here crosses path separators, unlike the one in the repository allow-list. That is
/// not an inconsistency to tidy up: a client sends the patterns it already holds in
/// <c>StatusBranchNamePatterns</c>, and those were written for <c>git branch --list</c>, which uses
/// fnmatch without <c>FNM_PATHNAME</c>. A pattern such as <c>origin/release/*</c> has to keep meaning
/// what it already means, or every existing project configuration silently stops matching the
/// branches it used to.
/// <para>
/// Matched directly rather than by translating to a regular expression. The pattern arrives in a
/// request body or a query string, so it is caller-controlled, and a caller-controlled regular
/// expression is a denial of service waiting to be written even when a match timeout is set. This
/// matcher backtracks at most once per wildcard and cannot be made to run long.
/// </para>
/// <para>
/// Matching is case sensitive, because git ref names are.
/// </para>
/// </remarks>
public sealed class BranchPattern
{
	/// <summary>
	/// The longest pattern that will be accepted.
	/// </summary>
	/// <remarks>
	/// A branch name that git will accept is far shorter than this. The limit exists so a request
	/// cannot carry an enormous pattern for every one of many branches.
	/// </remarks>
	private const int MaximumLength = 512;

	private BranchPattern(string text) => Text = text;

	/// <summary>Gets the pattern as the client sent it.</summary>
	public string Text { get; }

	/// <summary>
	/// Accepts a pattern, reporting why it was refused when it is.
	/// </summary>
	/// <param name="pattern">The pattern as the client sent it.</param>
	/// <param name="parsed">The pattern, when it was acceptable.</param>
	/// <param name="failure">Why the pattern was refused, when it was.</param>
	/// <returns><see langword="true"/> when the pattern was acceptable.</returns>
	public static bool TryParse(string? pattern, out BranchPattern? parsed, out string? failure)
	{
		parsed = null;
		failure = null;

		if (string.IsNullOrWhiteSpace(pattern))
		{
			failure = "a branch pattern must not be empty";
			return false;
		}

		if (pattern.Length > MaximumLength)
		{
			failure = $"a branch pattern must be shorter than {MaximumLength} characters";
			return false;
		}

		parsed = new BranchPattern(pattern);
		return true;
	}

	/// <summary>Reports whether a branch name matches.</summary>
	/// <param name="branchName">The branch name, including its remote prefix.</param>
	/// <returns><see langword="true"/> when it matches.</returns>
	public bool Matches(string branchName)
	{
		Ensure.NotNull(branchName);
		return Matches(Text.AsSpan(), branchName.AsSpan());
	}

	/// <summary>
	/// Matches a pattern against a name.
	/// </summary>
	/// <remarks>
	/// The classic linear wildcard match: walk both sides together, and on a mismatch fall back to the
	/// most recent star and let it consume one more character. Because only the most recent star is
	/// ever revisited, the work is bounded by the product of the two lengths in the worst case and is
	/// linear in practice, with no recursion and nothing to backtrack exponentially.
	/// </remarks>
	private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
	{
		int patternIndex = 0;
		int nameIndex = 0;
		int starIndex = -1;
		int resumeIndex = 0;

		while (nameIndex < name.Length)
		{
			if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || pattern[patternIndex] == name[nameIndex]))
			{
				patternIndex++;
				nameIndex++;
			}
			else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
			{
				starIndex = patternIndex;
				resumeIndex = nameIndex;
				patternIndex++;
			}
			else if (starIndex >= 0)
			{
				patternIndex = starIndex + 1;
				resumeIndex++;
				nameIndex = resumeIndex;
			}
			else
			{
				return false;
			}
		}

		while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
		{
			patternIndex++;
		}

		return patternIndex == pattern.Length;
	}
}
