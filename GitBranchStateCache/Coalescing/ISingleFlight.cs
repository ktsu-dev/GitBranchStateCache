// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Coalescing;

/// <summary>
/// Ensures only one operation per key is in flight at a time.
/// </summary>
/// <remarks>
/// Every open editor in the studio asks about the same handful of repositories on the same thirty
/// second heartbeat, so without this a stale mirror would be fetched once per client rather than once.
/// <para>
/// Instances do not share keys with each other, so two subsystems using this hold their own and
/// cannot collide however they spell a key.
/// </para>
/// </remarks>
public interface ISingleFlight
{
	/// <summary>
	/// Joins or starts the operation for one key.
	/// </summary>
	/// <param name="key">What is being coalesced.</param>
	/// <returns>
	/// A ticket that is either the leader, which must do the work and report the outcome, or a
	/// follower, which waits for the leader.
	/// </returns>
	public IWorkTicket Acquire(string key);
}
