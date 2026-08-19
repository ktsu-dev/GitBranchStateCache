// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Upstreams;

using System.Text.RegularExpressions;

/// <summary>
/// One allow-list pattern, translated once into something that can be matched.
/// </summary>
/// <remarks>
/// Shared by the allow-list and the options validator so that the rule deciding which patterns are
/// acceptable lives in exactly one place. A pattern the validator accepted and the matcher rejected,
/// or the reverse, would be a configuration that starts and then refuses everything.
/// </remarks>
public sealed class RepositoryPattern
{
	/// <summary>
	/// How long a single match may run before it is abandoned.
	/// </summary>
	/// <remarks>
	/// The translated patterns have no backtracking construct, so this cannot fire in practice. It is
	/// set because a regular expression built from configuration and run against a request path is
	/// exactly the shape that should carry a timeout.
	/// </remarks>
	private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

	private readonly Regex _expression;

	private RepositoryPattern(string text, Regex expression)
	{
		Text = text;
		_expression = expression;
	}

	/// <summary>Gets the pattern as configured.</summary>
	public string Text { get; }

	/// <summary>
	/// Translates a configured pattern, reporting why it was refused when it is.
	/// </summary>
	/// <remarks>
	/// A pattern naming no literal segment is refused, which is what makes a bare double star
	/// unspellable here. On a service that mirrors, "allow whatever anyone asks for" is not a
	/// configuration anyone should be able to arrive at by accident, because the cost of one request
	/// against an unlisted repository is a permanent clone of it.
	/// </remarks>
	/// <param name="pattern">The pattern as configured.</param>
	/// <param name="parsed">The translated pattern, when it was acceptable.</param>
	/// <param name="failure">Why the pattern was refused, when it was.</param>
	/// <returns><see langword="true"/> when the pattern was acceptable.</returns>
	public static bool TryParse(string? pattern, out RepositoryPattern? parsed, out string? failure)
	{
		parsed = null;
		failure = null;

		if (string.IsNullOrWhiteSpace(pattern))
		{
			failure = "must not be empty";
			return false;
		}

		string trimmed = pattern.Trim('/');
		string[] segments = trimmed.Split('/');

		if (segments.Any(string.IsNullOrEmpty))
		{
			failure = "must not contain an empty path segment";
			return false;
		}

		if (!segments.Any(IsLiteral))
		{
			failure =
				"must name at least one literal path segment, because one request for a repository this "
				+ "service has not seen creates a permanent mirror clone of it";
			return false;
		}

		parsed = new RepositoryPattern(pattern, Translate(trimmed));
		return true;
	}

	/// <summary>
	/// Reports whether a repository path matches.
	/// </summary>
	/// <param name="repositoryPath">The path following the upstream key, without surrounding slashes.</param>
	/// <returns><see langword="true"/> when it matches.</returns>
	public bool Matches(string repositoryPath) => _expression.IsMatch(repositoryPath);

	private static bool IsLiteral(string segment) =>
		segment.Length > 0 && !segment.Contains('*', StringComparison.Ordinal);

	/// <summary>
	/// Translates a glob pattern to an anchored regular expression.
	/// </summary>
	/// <remarks>
	/// The pattern is escaped first so every character is literal, then the two wildcards are put
	/// back. Order matters: a double star is restored before a single one, otherwise the first star of
	/// a double would be consumed as a single. Escaping produces a backslash-star pair, and the
	/// replacement for the double contains no backslash, so the second replacement cannot reach inside
	/// it.
	/// <para>
	/// Matching is case insensitive because forge repository names are, and a pattern that fails only
	/// because someone typed <c>Studio</c> is a support ticket rather than a control.
	/// </para>
	/// </remarks>
	private static Regex Translate(string pattern)
	{
		string expression = Regex.Escape(pattern)
			.Replace(@"\*\*", ".*", StringComparison.Ordinal)
			.Replace(@"\*", "[^/]*", StringComparison.Ordinal);

		return new Regex(
			$"^{expression}$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			MatchTimeout);
	}
}
