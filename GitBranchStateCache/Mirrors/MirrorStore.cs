// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

using System.Globalization;
using System.IO.Abstractions;
using ktsu.GitBranchStateCache.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Lays mirrors out under the configured root and records how current each one is.
/// </summary>
/// <remarks>
/// The layout is <c>&lt;root&gt;/&lt;upstream&gt;/&lt;repository path&gt;/mirror.git</c>, with the
/// repository path kept as directories so an operator looking at the volume can tell what is on it.
/// <para>
/// Freshness is recorded in marker files rather than inferred from modification times. A fetch that
/// finds nothing new touches nothing, so a mirror that is perfectly current would otherwise look
/// steadily more stale and be refetched forever.
/// </para>
/// </remarks>
/// <param name="fileSystem">The filesystem holding the mirrors.</param>
/// <param name="options">The configured options.</param>
/// <param name="timeProvider">Clock, injected so freshness is testable.</param>
public sealed class MirrorStore(
	IFileSystem fileSystem,
	IOptions<GitBranchStateCacheOptions> options,
	TimeProvider timeProvider) : IMirrorStore
{
	/// <summary>The directory name every mirror repository has.</summary>
	internal const string MirrorDirectoryName = "mirror.git";

	/// <summary>What a clone's staging directory is named before the suffix that makes it unique.</summary>
	internal const string StagingPrefix = MirrorDirectoryName + ".tmp-";

	/// <summary>The length of the unique suffix: a GUID written as 32 hexadecimal digits.</summary>
	private const int StagingSuffixLength = 32;

	private const string FetchedMarker = ".refs-fetched-at";
	private const string UsedMarker = ".last-used";

	/// <summary>
	/// The longest a single path segment may be.
	/// </summary>
	/// <remarks>
	/// Every filesystem this could run on stops well before this, and a segment near it is a sign of a
	/// path being constructed rather than named.
	/// </remarks>
	private const int MaximumSegmentLength = 128;

	/// <inheritdoc />
	public bool TryResolve(MirrorKey key, out string? directory)
	{
		Ensure.NotNull(key);
		directory = null;

		if (!IsSafeSegment(key.Upstream))
		{
			return false;
		}

		string[] segments = key.RepositoryPath.Trim('/').Split('/');

		if (segments.Length == 0 || !segments.All(IsSafeSegment))
		{
			return false;
		}

		string combined = options.Value.MirrorRoot;
		combined = fileSystem.Path.Combine(combined, key.Upstream);

		foreach (string segment in segments)
		{
			combined = fileSystem.Path.Combine(combined, segment);
		}

		directory = fileSystem.Path.Combine(combined, MirrorDirectoryName);
		return true;
	}

	/// <inheritdoc />
	public bool Exists(string directory) => fileSystem.Directory.Exists(directory);

	/// <inheritdoc />
	public DateTimeOffset? RefsFetchedAt(string directory) => ReadMarker(directory, FetchedMarker);

	/// <inheritdoc />
	public void MarkFetched(string directory) => WriteMarker(directory, FetchedMarker);

	/// <inheritdoc />
	public void MarkUsed(string directory) => WriteMarker(directory, UsedMarker);

	/// <inheritdoc />
	public DateTimeOffset? LastUsedAt(string directory) => ReadMarker(directory, UsedMarker);

	/// <inheritdoc />
	/// <remarks>
	/// A directory named <c>mirror.git</c> is not necessarily a mirror. Clients send repository paths
	/// with a <c>.git</c> suffix, so the repository <c>studio/mirror</c> lives at
	/// <c>studio/mirror.git/mirror.git</c>, and its parent is only a path segment. Such a directory is
	/// recognised by holding another candidate while having no files of its own: a real mirror is a
	/// bare repository and always has at least <c>HEAD</c>. Left in, the parent would never carry a
	/// marker, would look idle from the day it was created, and would be reaped along with the live
	/// mirror inside it.
	/// </remarks>
	public IReadOnlyList<string> Enumerate()
	{
		string root = options.Value.MirrorRoot;

		if (!fileSystem.Directory.Exists(root))
		{
			return [];
		}

		string[] candidates = fileSystem.Directory.GetDirectories(root, MirrorDirectoryName, SearchOption.AllDirectories);

		return [.. candidates.Where(candidate => !IsRepositoryPathSegment(candidate, candidates))];
	}

	/// <inheritdoc />
	/// <remarks>
	/// Matched on the whole name a clone gives its staging directory, the prefix and a GUID's 32
	/// hexadecimal digits, rather than on the prefix alone. A repository path segment may begin
	/// <c>mirror.git.tmp-</c> too, and that directory holds live mirrors.
	/// </remarks>
	public IReadOnlyList<string> EnumerateStaging()
	{
		string root = options.Value.MirrorRoot;

		if (!fileSystem.Directory.Exists(root))
		{
			return [];
		}

		return [.. fileSystem.Directory
			.GetDirectories(root, StagingPrefix + "*", SearchOption.AllDirectories)
			.Where(candidate => IsStagingName(fileSystem.Path.GetFileName(candidate)))];
	}

	/// <summary>
	/// Reports whether a directory name is one a clone gives its staging directory.
	/// </summary>
	/// <param name="name">The directory name.</param>
	/// <returns><see langword="true"/> when it is the staging prefix followed by 32 hexadecimal digits.</returns>
	internal static bool IsStagingName(string name) =>
		name.Length == StagingPrefix.Length + StagingSuffixLength
		&& name.StartsWith(StagingPrefix, StringComparison.Ordinal)
		&& name[StagingPrefix.Length..].All(char.IsAsciiHexDigit);

	/// <summary>
	/// Reports whether a path lies strictly inside a directory.
	/// </summary>
	/// <param name="path">The path that may be inside.</param>
	/// <param name="directory">The directory that may contain it.</param>
	/// <returns><see langword="true"/> when <paramref name="path"/> is below <paramref name="directory"/>.</returns>
	internal static bool IsInside(string path, string directory) =>
		path.Length > directory.Length + 1
		&& path.StartsWith(directory, StringComparison.Ordinal)
		&& path[directory.Length] is '/' or '\\';

	/// <inheritdoc />
	public void Delete(string directory)
	{
		if (fileSystem.Directory.Exists(directory))
		{
			fileSystem.Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>
	/// Reports whether one path segment can be used as a directory name as it stands.
	/// </summary>
	/// <remarks>
	/// An allow-list of characters rather than an escape or a substitution. Substituting would map two
	/// different repositories onto one directory, and escaping would produce names nobody can read on
	/// the volume; refusing costs nothing, because every repository path a forge actually issues is
	/// already within this set.
	/// <para>
	/// This is the second line of defence and not the first: the repository allow-list has already
	/// refused anything not explicitly configured. It exists because a traversal that reaches the
	/// filesystem is not the place to discover that the first line had a gap.
	/// </para>
	/// </remarks>
	private static bool IsSafeSegment(string segment)
	{
		if (segment.Length is 0 or > MaximumSegmentLength)
		{
			return false;
		}

		// A leading dot covers "." and ".." without special casing either, and also keeps mirrors from
		// being written as hidden directories.
		if (segment[0] is '.' or '-')
		{
			return false;
		}

		return segment.All(character =>
			char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
	}

	private bool IsRepositoryPathSegment(string candidate, string[] candidates) =>
		candidates.Any(other => IsInside(other, candidate))
		&& fileSystem.Directory.GetFiles(candidate).Length == 0;

	private DateTimeOffset? ReadMarker(string directory, string marker)
	{
		string path = fileSystem.Path.Combine(directory, marker);

		if (!fileSystem.File.Exists(path))
		{
			return null;
		}

		string text = fileSystem.File.ReadAllText(path);

		return DateTimeOffset.TryParse(
			text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out DateTimeOffset parsed)
			? parsed
			: null;
	}

	/// <summary>
	/// Records an instant against a mirror, tolerating a write that does not land.
	/// </summary>
	/// <remarks>
	/// A marker that cannot be written costs an extra fetch or an extra day before a reaper notices
	/// the mirror. Neither is worth failing a request that has otherwise succeeded.
	/// </remarks>
	private void WriteMarker(string directory, string marker)
	{
		try
		{
			fileSystem.Directory.CreateDirectory(directory);
			fileSystem.File.WriteAllText(
				fileSystem.Path.Combine(directory, marker),
				timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			// Deliberately swallowed. See the remarks above.
		}
	}
}
