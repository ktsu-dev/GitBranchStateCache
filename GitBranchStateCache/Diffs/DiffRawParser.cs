// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

/// <summary>
/// Reads the raw, NUL-delimited output of <c>git diff-tree</c>.
/// </summary>
/// <remarks>
/// The <c>-z</c> form is used rather than the default. Without it git quotes any path containing a
/// space, a quote, a backslash, or a byte outside ASCII, using C-style escapes that then have to be
/// unquoted correctly; with it the path is emitted verbatim between NUL bytes. Since a mistake in
/// unquoting means reporting a path that matches nothing on the client, and therefore failing to warn
/// about a stale asset, the format that has nothing to unquote is the right one to depend on.
/// <para>
/// A record shape this parser does not recognise raises <see cref="DiffFormatException"/>. Nothing is
/// ever skipped.
/// </para>
/// </remarks>
public static class DiffRawParser
{
	/// <summary>The two object id lengths git can produce, for SHA-1 and SHA-256 repositories.</summary>
	private static readonly int[] ObjectIdLengths = [40, 64];

	/// <summary>
	/// Parses raw diff output.
	/// </summary>
	/// <param name="output">The command's standard output.</param>
	/// <returns>Every path that differs, in the order git reported them.</returns>
	/// <exception cref="DiffFormatException">The output was not in the expected form.</exception>
	public static IReadOnlyList<DiffEntry> Parse(string output)
	{
		Ensure.NotNull(output);

		List<DiffEntry> entries = [];
		string[] tokens = output.Split('\0');
		int index = 0;

		while (index < tokens.Length)
		{
			string token = tokens[index];

			// The output ends with a NUL, so the split leaves one trailing empty token. Anything empty
			// before the end is a record that is not there.
			if (token.Length == 0)
			{
				if (index != tokens.Length - 1)
				{
					throw new DiffFormatException(
						$"Raw diff output had an empty record at position {index}.");
				}

				break;
			}

			index = ReadRecord(tokens, index, entries);
		}

		return entries;
	}

	/// <summary>
	/// Reads one record and returns the index the next one starts at.
	/// </summary>
	private static int ReadRecord(string[] tokens, int index, List<DiffEntry> entries)
	{
		char status = ReadStatus(tokens[index], out string? destinationBlob);

		// A rename or a copy carries a similarity score and names two paths. Rename detection is turned
		// off for the invocation this parses, so these should not appear; they are handled anyway
		// because a configuration change that turned detection back on must not become a parse failure
		// in production.
		int paths = status is 'R' or 'C' ? 2 : 1;

		if (index + paths >= tokens.Length)
		{
			throw new DiffFormatException(
				$"Raw diff record '{tokens[index]}' named {paths} path(s) but the output ended first.");
		}

		if (paths == 2)
		{
			string source = RequirePath(tokens[index + 1]);
			string destination = RequirePath(tokens[index + 2]);

			// The source path no longer exists at the second commit, so from a client's point of view
			// it was deleted, and the destination is what carries the content.
			entries.Add(new DiffEntry(source, null, 'D'));
			entries.Add(new DiffEntry(destination, destinationBlob, status));
		}
		else
		{
			entries.Add(new DiffEntry(RequirePath(tokens[index + 1]), destinationBlob, status));
		}

		return index + paths + 1;
	}

	/// <summary>
	/// Reads the metadata half of a record.
	/// </summary>
	/// <remarks>
	/// The shape is <c>:srcmode dstmode srcblob dstblob status</c>. Every field is checked rather than
	/// only the ones that are used, because a record this parser half-understands is a record it might
	/// be reading the wrong field of.
	/// </remarks>
	private static char ReadStatus(string metadata, out string? destinationBlob)
	{
		if (metadata[0] != ':')
		{
			throw new DiffFormatException($"Raw diff record '{Describe(metadata)}' did not begin with ':'.");
		}

		string[] fields = metadata[1..].Split(' ');

		if (fields.Length != 5)
		{
			throw new DiffFormatException(
				$"Raw diff record '{Describe(metadata)}' had {fields.Length} fields where five were expected.");
		}

		if (!IsMode(fields[0]) || !IsMode(fields[1]))
		{
			throw new DiffFormatException($"Raw diff record '{Describe(metadata)}' had a malformed file mode.");
		}

		if (!IsObjectId(fields[2]) || !IsObjectId(fields[3]))
		{
			throw new DiffFormatException($"Raw diff record '{Describe(metadata)}' had a malformed object id.");
		}

		if (fields[4].Length == 0 || !char.IsAsciiLetterUpper(fields[4][0]))
		{
			throw new DiffFormatException($"Raw diff record '{Describe(metadata)}' had no status letter.");
		}

		// An all-zero object id means the path does not exist on that side, which for the second commit
		// means it was deleted.
		destinationBlob = IsAbsent(fields[3]) ? null : fields[3];
		return fields[4][0];
	}

	private static string RequirePath(string path) =>
		path.Length > 0 ? path : throw new DiffFormatException("Raw diff output named an empty path.");

	private static bool IsMode(string field) =>
		field.Length == 6 && field.All(character => character is >= '0' and <= '7');

	private static bool IsObjectId(string field) =>
		ObjectIdLengths.Contains(field.Length) && field.All(char.IsAsciiHexDigitLower);

	private static bool IsAbsent(string objectId) => objectId.All(character => character == '0');

	/// <summary>
	/// Trims a record for an exception message.
	/// </summary>
	/// <remarks>
	/// Bounded because the string being described is whatever git emitted, and an unbounded copy of it
	/// in a message that reaches a log is a way for one malformed record to become a large problem.
	/// </remarks>
	private static string Describe(string record) => record.Length <= 120 ? record : record[..120];
}
