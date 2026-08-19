// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Upstreams;

using ktsu.GitBranchStateCache.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Resolves upstream keys from <see cref="GitBranchStateCacheOptions.Upstreams"/>.
/// </summary>
/// <remarks>
/// The lookup is built once in the constructor rather than walked on each request. Any upstream with
/// no base URL is dropped here rather than guarded against on every call, which is safe because the
/// options validator has already refused that configuration at startup.
/// </remarks>
/// <param name="options">The configured options.</param>
public sealed class UpstreamRegistry(IOptions<GitBranchStateCacheOptions> options) : IUpstreamRegistry
{
	private readonly Dictionary<string, Uri> _upstreams = options.Value.Upstreams
		.Where(pair => pair.Value.BaseUrl is not null)
		.ToDictionary(
			pair => pair.Key,
			pair => pair.Value.BaseUrl!,
			StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	public bool TryResolve(string key, out Uri? baseUrl)
	{
		baseUrl = null;

		if (string.IsNullOrEmpty(key) || key.Contains('/', StringComparison.Ordinal))
		{
			return false;
		}

		if (_upstreams.TryGetValue(key, out Uri? resolved))
		{
			baseUrl = resolved;
			return true;
		}

		return false;
	}
}
