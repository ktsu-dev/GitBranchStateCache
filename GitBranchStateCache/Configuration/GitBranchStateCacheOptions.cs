// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Configuration;

/// <summary>
/// Root configuration for the branch state cache.
/// </summary>
/// <remarks>
/// Collection properties are getter-only with an initializer. The configuration binder populates
/// them in place, and a settable collection property would trip CA2227 under warnings-as-errors.
/// </remarks>
public sealed class GitBranchStateCacheOptions
{
	/// <summary>
	/// The configuration section these options bind from.
	/// </summary>
	public const string SectionName = "GitBranchStateCache";

	/// <summary>
	/// Gets or sets the directory holding one bare mirror per repository.
	/// </summary>
	public string MirrorRoot { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the git executable to invoke.
	/// </summary>
	/// <remarks>
	/// Resolved through <c>PATH</c> when it is a bare name. Configurable so a deployment can pin an
	/// absolute path, and so tests can substitute a process whose behaviour they control.
	/// </remarks>
	public string GitExecutable { get; set; } = "git";

	/// <summary>
	/// Gets or sets the remote name every branch is reported under.
	/// </summary>
	/// <remarks>
	/// The mirror holds one upstream's branches and nothing else, so it has no remotes in the sense a
	/// working clone does. Clients do: the plugin obtains branch names from
	/// <c>git branch --remotes</c> and <c>git rev-parse --abbrev-ref @{u}</c>, both of which spell them
	/// <c>origin/main</c>. Reporting branches under this prefix is what lets a client send the patterns
	/// it already has, unmodified, and compare the names it gets back against the names it already
	/// holds.
	/// </remarks>
	public string RemoteName { get; set; } = "origin";

	/// <summary>
	/// Gets or sets how old a mirror's refs may be before a request fetches.
	/// </summary>
	public TimeSpan RefsTtl { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Gets or sets how long an upstream authorization is trusted before it is proven again.
	/// </summary>
	/// <remarks>
	/// This is the window in which a credential revoked upstream can still read branch state. It
	/// grants nothing else, and never object content.
	/// </remarks>
	public TimeSpan AdmissionTtl { get; set; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Gets or sets how long a clone or fetch may run before it is killed.
	/// </summary>
	public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromMinutes(2);

	/// <summary>
	/// Gets or sets how long a diff may run before it is killed and its branch reported as failed.
	/// </summary>
	public TimeSpan DiffTimeout { get; set; } = TimeSpan.FromMinutes(2);

	/// <summary>
	/// Gets or sets how long the admission probe may run before it is killed.
	/// </summary>
	/// <remarks>
	/// Separate from <see cref="FetchTimeout"/> because the two are not the same question. A fetch may
	/// legitimately take minutes on a repository this service has never seen; an <c>ls-remote</c> that
	/// takes more than a few seconds means the forge is not answering, and every request is waiting on
	/// it before anything else can happen.
	/// </remarks>
	public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Gets or sets how many computed diffs are retained before the least recently used is evicted.
	/// </summary>
	public int MaxCachedDiffs { get; set; } = 2000;

	/// <summary>
	/// Gets or sets the most paths one request may name, and the most one response may return.
	/// </summary>
	public int MaxPathsPerRequest { get; set; } = 20_000;

	/// <summary>
	/// Gets or sets how long a mirror may go unqueried before it is deleted.
	/// </summary>
	/// <remarks>
	/// The allow-list bounds which repositories may ever be mirrored, but not for how long. An
	/// allow-listed repository that stops being queried otherwise keeps its mirror forever, so disk
	/// use ratchets and never falls. A deleted mirror costs one clone if the repository is queried
	/// again, which is the cheapest possible way to be wrong about this.
	/// <para>
	/// Set to <see cref="TimeSpan.Zero"/> to keep every mirror indefinitely.
	/// </para>
	/// </remarks>
	public TimeSpan MirrorIdleMaxAge { get; set; } = TimeSpan.FromDays(30);

	/// <summary>
	/// Gets or sets how often idle mirrors are swept for.
	/// </summary>
	public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromHours(1);

	/// <summary>
	/// Gets the configured upstreams, keyed by the first path segment clients address them by.
	/// </summary>
	public IDictionary<string, UpstreamOptions> Upstreams { get; } =
		new Dictionary<string, UpstreamOptions>(StringComparer.OrdinalIgnoreCase);
}
