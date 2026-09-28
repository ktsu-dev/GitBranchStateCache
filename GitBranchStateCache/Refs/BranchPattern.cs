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
/// The same constructs git's wildmatch honours for <c>git branch --list</c> are honoured here:
/// <c>*</c>, <c>?</c>, bracket expressions such as <c>[0-9]</c>, <c>[abc]</c>, <c>[!x]</c> and
/// <c>[^x]</c> (a <c>]</c> placed first is literal), and <c>\</c> to escape the next character. A
/// <c>[</c> with no closing <c>]</c> is an ordinary character.
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
	/// ever revisited, the work is bounded by the product of the two lengths (times the length of a
	/// bracket expression, which is itself bounded by the pattern) and is linear in practice, with no
	/// recursion and nothing to backtrack exponentially.
	/// </remarks>
	private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
	{
		int patternIndex = 0;
		int nameIndex = 0;
		int starIndex = -1;
		int resumeIndex = 0;

		while (nameIndex < name.Length)
		{
			if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
			{
				starIndex = patternIndex;
				resumeIndex = nameIndex;
				patternIndex++;
			}
			else if (patternIndex < pattern.Length && MatchesOne(pattern, patternIndex, name[nameIndex], out int length))
			{
				patternIndex += length;
				nameIndex++;
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

	/// <summary>
	/// Matches the one pattern element starting at <paramref name="index"/> against one character.
	/// </summary>
	/// <param name="pattern">The whole pattern.</param>
	/// <param name="index">Where the element starts. It is not a star.</param>
	/// <param name="character">The character from the name.</param>
	/// <param name="length">How many pattern characters the element spans.</param>
	/// <returns><see langword="true"/> when the element matches the character.</returns>
	private static bool MatchesOne(ReadOnlySpan<char> pattern, int index, char character, out int length)
	{
		char element = pattern[index];

		if (element == '?')
		{
			length = 1;
			return true;
		}

		if (element == '\\' && index + 1 < pattern.Length)
		{
			length = 2;
			return pattern[index + 1] == character;
		}

		if (element == '[' && TryMatchBracket(pattern, index, character, out length, out bool matched))
		{
			return matched;
		}

		length = 1;
		return element == character;
	}

	/// <summary>
	/// Matches a bracket expression such as <c>[0-9]</c> or <c>[!abc]</c> against one character.
	/// </summary>
	/// <param name="pattern">The whole pattern.</param>
	/// <param name="start">Where the opening <c>[</c> is.</param>
	/// <param name="character">The character from the name.</param>
	/// <param name="length">How many pattern characters the expression spans, including both brackets.</param>
	/// <param name="matched">Whether the expression matches the character.</param>
	/// <returns>
	/// <see langword="false"/> when the bracket is never closed, in which case the <c>[</c> is an
	/// ordinary character.
	/// </returns>
	private static bool TryMatchBracket(ReadOnlySpan<char> pattern, int start, char character, out int length, out bool matched)
	{
		int index = start + 1;
		bool negated = index < pattern.Length && (pattern[index] == '!' || pattern[index] == '^');
		if (negated)
		{
			index++;
		}

		bool found = false;
		bool first = true;

		// A ] straight after the opening bracket (or its negation) is a member, not the end.
		while (index < pattern.Length && (first || pattern[index] != ']'))
		{
			first = false;

			if (!TryReadRange(pattern, ref index, out char low, out char high))
			{
				break;
			}

			found |= low <= character && character <= high;
		}

		if (index >= pattern.Length)
		{
			length = 1;
			matched = false;
			return false;
		}

		length = index + 1 - start;
		matched = found != negated;
		return true;
	}

	/// <summary>
	/// Reads one member of a bracket expression: a single character, or a range such as <c>a-z</c>.
	/// </summary>
	/// <param name="pattern">The whole pattern.</param>
	/// <param name="index">Where the member starts; moved past it.</param>
	/// <param name="low">The first character the member covers.</param>
	/// <param name="high">The last character the member covers; equal to <paramref name="low"/> for a single character.</param>
	/// <returns>
	/// <see langword="false"/> when the pattern ends in a lone backslash, which leaves the bracket
	/// unclosed.
	/// </returns>
	/// <remarks>
	/// A <c>-</c> directly before the closing <c>]</c> is an ordinary member, not the start of a range.
	/// </remarks>
	private static bool TryReadRange(ReadOnlySpan<char> pattern, ref int index, out char low, out char high)
	{
		if (!TryReadMember(pattern, ref index, out low))
		{
			high = default;
			return false;
		}

		high = low;
		if (index + 1 < pattern.Length && pattern[index] == '-' && pattern[index + 1] != ']')
		{
			index++;
			return TryReadMember(pattern, ref index, out high);
		}

		return true;
	}

	/// <summary>
	/// Reads one member character of a bracket expression, honouring a backslash escape.
	/// </summary>
	/// <param name="pattern">The whole pattern.</param>
	/// <param name="index">Where the member starts; moved past it.</param>
	/// <param name="value">The member character.</param>
	/// <returns>
	/// <see langword="false"/> when the pattern ends in a lone backslash, which leaves the bracket
	/// unclosed.
	/// </returns>
	private static bool TryReadMember(ReadOnlySpan<char> pattern, ref int index, out char value)
	{
		if (pattern[index] == '\\')
		{
			if (index + 1 >= pattern.Length)
			{
				index = pattern.Length;
				value = default;
				return false;
			}

			value = pattern[index + 1];
			index += 2;
			return true;
		}

		value = pattern[index];
		index++;
		return true;
	}
}
