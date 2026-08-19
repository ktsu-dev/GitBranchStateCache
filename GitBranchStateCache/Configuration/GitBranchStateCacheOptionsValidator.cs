// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Configuration;

using ktsu.GitBranchStateCache.Upstreams;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="GitBranchStateCacheOptions"/> at startup.
/// </summary>
/// <remarks>
/// Every problem is collected rather than reported one at a time, because an operator fixing
/// configuration by trial and error across restarts is a poor use of their afternoon. Mirror root
/// writability is checked separately, since touching the filesystem from an options validator runs at
/// an awkward point in the host lifetime.
/// </remarks>
public sealed class GitBranchStateCacheOptionsValidator : IValidateOptions<GitBranchStateCacheOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, GitBranchStateCacheOptions options)
	{
		Ensure.NotNull(options);

		List<string> failures = [];

		ValidateUpstreams(options, failures);
		ValidateMirror(options, failures);
		ValidateTimings(options, failures);
		ValidateLimits(options, failures);

		return failures.Count == 0
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(failures);
	}

	private static void ValidateUpstreams(GitBranchStateCacheOptions options, List<string> failures)
	{
		if (options.Upstreams.Count == 0)
		{
			failures.Add($"{GitBranchStateCacheOptions.SectionName}:Upstreams must contain at least one upstream.");
		}

		foreach ((string key, UpstreamOptions upstream) in options.Upstreams)
		{
			if (!IsAbsoluteHttpUrl(upstream.BaseUrl))
			{
				failures.Add(
					$"{GitBranchStateCacheOptions.SectionName}:Upstreams:{key}:BaseUrl must be an absolute http or https URL, but was '{upstream.BaseUrl}'.");
			}

			ValidateRepositories(key, upstream, failures);
		}
	}

	/// <summary>
	/// Requires each upstream to say which repositories it may mirror.
	/// </summary>
	/// <remarks>
	/// Required rather than defaulting to everything, and unlike <c>ktsu.GitLfsCache</c> there is no
	/// wildcard meaning everything. The object cache's worst case for an unlisted repository is cache
	/// warmth spent on content nobody wanted; this service's worst case is a permanent clone of a
	/// repository it was never deployed for, created by a single request and never evicted. That is
	/// why the message names a concrete example rather than an escape hatch: there is none.
	/// </remarks>
	private static void ValidateRepositories(string key, UpstreamOptions upstream, List<string> failures)
	{
		string setting = $"{GitBranchStateCacheOptions.SectionName}:Upstreams:{key}:Repositories";

		if (upstream.Repositories.Count == 0)
		{
			failures.Add(
				$"{setting} must contain at least one repository path pattern, for example 'studio/game.git'. "
				+ "There is no pattern meaning every repository: one request creates a permanent mirror clone.");
			return;
		}

		for (int index = 0; index < upstream.Repositories.Count; index++)
		{
			if (!RepositoryPattern.TryParse(upstream.Repositories[index], out _, out string? failure))
			{
				failures.Add($"{setting}[{index}] {failure}, but was '{upstream.Repositories[index]}'.");
			}
		}
	}

	private static void ValidateMirror(GitBranchStateCacheOptions options, List<string> failures)
	{
		if (string.IsNullOrWhiteSpace(options.MirrorRoot))
		{
			failures.Add(
				$"{GitBranchStateCacheOptions.SectionName}:MirrorRoot must be set to an absolute directory path.");
		}
		else if (!Path.IsPathFullyQualified(options.MirrorRoot))
		{
			// Caught here rather than left to the mirror store, so a mistyped path becomes a startup
			// message naming the setting and showing the value rather than a constructor exception.
			failures.Add(
				$"{GitBranchStateCacheOptions.SectionName}:MirrorRoot must be a fully qualified absolute path for this platform, but was '{options.MirrorRoot}'.");
		}

		if (string.IsNullOrWhiteSpace(options.GitExecutable))
		{
			failures.Add($"{GitBranchStateCacheOptions.SectionName}:GitExecutable must not be empty.");
		}

		if (string.IsNullOrWhiteSpace(options.RemoteName)
			|| options.RemoteName.Contains('/', StringComparison.Ordinal))
		{
			failures.Add(
				$"{GitBranchStateCacheOptions.SectionName}:RemoteName must be a single path segment such as 'origin', but was '{options.RemoteName}'.");
		}
	}

	private static void ValidateTimings(GitBranchStateCacheOptions options, List<string> failures)
	{
		RequirePositive(options.RefsTtl, "RefsTtl", failures);
		RequirePositive(options.AdmissionTtl, "AdmissionTtl", failures);
		RequirePositive(options.FetchTimeout, "FetchTimeout", failures);
		RequirePositive(options.DiffTimeout, "DiffTimeout", failures);
		RequirePositive(options.ProbeTimeout, "ProbeTimeout", failures);
		RequirePositive(options.MaintenanceInterval, "MaintenanceInterval", failures);

		// Refs older than the authorization proving they may be read would mean serving state that
		// outlives the only evidence the caller was ever allowed to see it.
		if (options.RefsTtl > options.AdmissionTtl)
		{
			failures.Add(
				$"{GitBranchStateCacheOptions.SectionName}:RefsTtl ({options.RefsTtl}) must not exceed AdmissionTtl ({options.AdmissionTtl}), or branch state could outlive the authorization that permits reading it.");
		}

		// Zero is the documented way to keep every mirror indefinitely, so only a negative value is a
		// mistake.
		if (options.MirrorIdleMaxAge < TimeSpan.Zero)
		{
			failures.Add(
				$"{GitBranchStateCacheOptions.SectionName}:MirrorIdleMaxAge must not be negative. Use zero to keep every mirror indefinitely.");
		}
	}

	private static void ValidateLimits(GitBranchStateCacheOptions options, List<string> failures)
	{
		if (options.MaxCachedDiffs <= 0)
		{
			failures.Add($"{GitBranchStateCacheOptions.SectionName}:MaxCachedDiffs must be greater than zero.");
		}

		if (options.MaxPathsPerRequest <= 0)
		{
			failures.Add($"{GitBranchStateCacheOptions.SectionName}:MaxPathsPerRequest must be greater than zero.");
		}
	}

	private static void RequirePositive(TimeSpan value, string setting, List<string> failures)
	{
		if (value <= TimeSpan.Zero)
		{
			failures.Add($"{GitBranchStateCacheOptions.SectionName}:{setting} must be greater than zero.");
		}
	}

	/// <summary>
	/// Reports whether a bound URL is absolute and addressable over HTTP.
	/// </summary>
	/// <remarks>
	/// The configuration binder accepts almost any string as a relative URI, so a typo lands here as a
	/// relative value rather than failing during binding. That is deliberate: it means the operator
	/// gets this message, naming the setting, rather than a binder exception.
	/// </remarks>
	private static bool IsAbsoluteHttpUrl(Uri? candidate) =>
		candidate is not null
		&& candidate.IsAbsoluteUri
		&& (candidate.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps);
}
