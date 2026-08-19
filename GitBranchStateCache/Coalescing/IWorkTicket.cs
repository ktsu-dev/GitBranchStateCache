// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Coalescing;

/// <summary>
/// One request's place in a coalesced operation.
/// </summary>
public interface IWorkTicket : IDisposable
{
	/// <summary>
	/// Gets a value indicating whether this request must perform the work itself.
	/// </summary>
	public bool IsLeader { get; }

	/// <summary>
	/// Waits for the leader to finish.
	/// </summary>
	/// <param name="timeout">
	/// How long to wait before giving up on the leader. A follower that waits forever behind a stalled
	/// leader is worse than one that proceeds with what it has.
	/// </param>
	/// <param name="cancellationToken">Cancellation token, typically the client's disconnect.</param>
	/// <returns>
	/// <see langword="true"/> when the leader succeeded. <see langword="false"/> when it failed or did
	/// not finish in time, which the caller decides what to do about.
	/// </returns>
	public Task<bool> WaitForLeaderAsync(TimeSpan timeout, CancellationToken cancellationToken);

	/// <summary>
	/// Reports the outcome, releasing every waiting follower.
	/// </summary>
	/// <param name="succeeded">Whether the work completed successfully.</param>
	public void Complete(bool succeeded);
}
