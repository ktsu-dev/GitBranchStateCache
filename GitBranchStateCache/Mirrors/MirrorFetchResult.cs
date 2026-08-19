// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

/// <summary>
/// What state a mirror was left in by an attempt to bring it up to date.
/// </summary>
public enum MirrorFetchStatus
{
	/// <summary>The mirror's refs are as current as this service can make them.</summary>
	Current,

	/// <summary>
	/// The mirror exists and is usable, but the refs are older than they should be.
	/// </summary>
	/// <remarks>
	/// Served rather than refused, with the age reported, so a client can see the data is old and
	/// decide for itself. Refusing outright would make a brief forge outage look like every branch
	/// suddenly having nothing on it, which is the one wrong answer this service must never give.
	/// </remarks>
	Stale,

	/// <summary>There is no usable mirror and one could not be created.</summary>
	Unavailable,
}

/// <summary>
/// The outcome of bringing a mirror up to date.
/// </summary>
/// <param name="Status">What state the mirror is in.</param>
/// <param name="RefsAsOf">When the refs were last known to match the upstream, if ever.</param>
/// <param name="Failure">What went wrong, when something did.</param>
public sealed record MirrorFetchResult(MirrorFetchStatus Status, DateTimeOffset? RefsAsOf, string? Failure)
{
	/// <summary>Reports a mirror whose refs are current.</summary>
	/// <param name="refsAsOf">When the refs were fetched.</param>
	/// <returns>The result.</returns>
	public static MirrorFetchResult Current(DateTimeOffset refsAsOf) =>
		new(MirrorFetchStatus.Current, refsAsOf, null);

	/// <summary>Reports a usable mirror that could not be refreshed.</summary>
	/// <param name="refsAsOf">When the refs were last fetched, if ever.</param>
	/// <param name="failure">Why the refresh did not happen.</param>
	/// <returns>The result.</returns>
	public static MirrorFetchResult Stale(DateTimeOffset? refsAsOf, string failure) =>
		new(MirrorFetchStatus.Stale, refsAsOf, failure);

	/// <summary>Reports that no mirror could be made available.</summary>
	/// <param name="failure">Why not.</param>
	/// <returns>The result.</returns>
	public static MirrorFetchResult Unavailable(string failure) =>
		new(MirrorFetchStatus.Unavailable, null, failure);
}
