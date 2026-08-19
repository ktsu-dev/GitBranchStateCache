// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Upstreams;

using ktsu.GitBranchStateCache.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Matches repository paths against the patterns configured for each upstream.
/// </summary>
/// <remarks>
/// This is a resource control and not an access control. It never grants anything, and an allowed
/// repository is still served only to a caller whose own credential passed <c>ls-remote</c> upstream.
/// <para>
/// It has to be consulted <em>before</em> admission, not after. Reversed, an unlisted repository
/// would still be probed against the forge with the caller's credential before being refused, which
/// turns this service into an oracle for which repositories a credential can read.
/// </para>
/// <para>
/// Patterns that failed to parse are dropped here rather than guarded against on every call, which is
/// safe because the options validator has already refused that configuration at startup.
/// </para>
/// </remarks>
/// <param name="options">The configured options.</param>
public sealed class RepositoryAllowList(IOptions<GitBranchStateCacheOptions> options) : IRepositoryAllowList
{
	private readonly Dictionary<string, RepositoryPattern[]> _patterns = options.Value.Upstreams
		.ToDictionary(
			pair => pair.Key,
			pair => pair.Value.Repositories
				.Select(pattern => RepositoryPattern.TryParse(pattern, out RepositoryPattern? parsed, out _)
					? parsed
					: null)
				.OfType<RepositoryPattern>()
				.ToArray(),
			StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	public bool IsAllowed(string upstream, string repositoryPath)
	{
		Ensure.NotNull(upstream);
		Ensure.NotNull(repositoryPath);

		if (!_patterns.TryGetValue(upstream, out RepositoryPattern[]? patterns))
		{
			return false;
		}

		string candidate = repositoryPath.Trim('/');

		return patterns.Any(pattern => pattern.Matches(candidate));
	}
}
