// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Upstreams;

/// <summary>
/// Builds the upstream URL for a repository.
/// </summary>
public static class UpstreamUrl
{
	/// <summary>
	/// Appends a repository path to an upstream base URL.
	/// </summary>
	/// <remarks>
	/// Built by appending escaped segments rather than by handing the whole path to
	/// <see cref="Uri"/>, which resolves <c>..</c> and would let a repository path walk up out of the
	/// base and address a different part of the forge. Every segment is checked instead, and anything
	/// that could traverse is refused rather than normalized away.
	/// </remarks>
	/// <param name="baseUrl">The configured upstream base URL.</param>
	/// <param name="repositoryPath">The repository path following the upstream key.</param>
	/// <param name="url">The combined URL, when the path was acceptable.</param>
	/// <returns><see langword="true"/> when the path could be appended safely.</returns>
	public static bool TryCombine(Uri baseUrl, string repositoryPath, out Uri? url)
	{
		Ensure.NotNull(baseUrl);
		Ensure.NotNull(repositoryPath);

		url = null;

		string[] segments = repositoryPath.Trim('/').Split('/');

		if (segments.Length == 0 || segments.Any(segment =>
			segment.Length == 0 || segment is "." or ".." || segment.Contains('\\', StringComparison.Ordinal)))
		{
			return false;
		}

		string prefix = baseUrl.AbsoluteUri.TrimEnd('/');
		string suffix = string.Join('/', segments.Select(Uri.EscapeDataString));

		return Uri.TryCreate($"{prefix}/{suffix}", UriKind.Absolute, out url);
	}
}
