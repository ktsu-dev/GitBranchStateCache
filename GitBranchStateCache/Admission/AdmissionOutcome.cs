// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Admission;

/// <summary>
/// Whether a caller may be served, and what to tell them when they may not.
/// </summary>
/// <param name="Admitted">Whether the caller's own credential was proven against the upstream.</param>
/// <param name="StatusCode">The status to answer with when they were not.</param>
/// <param name="Reason">What to tell them when they were not.</param>
public sealed record AdmissionOutcome(bool Admitted, int StatusCode, string? Reason)
{
	/// <summary>Reports a caller the upstream accepted.</summary>
	/// <returns>The outcome.</returns>
	public static AdmissionOutcome Allow() => new(true, 200, null);

	/// <summary>Reports a caller the upstream refused.</summary>
	/// <param name="statusCode">The status to answer with.</param>
	/// <param name="reason">What to tell them.</param>
	/// <returns>The outcome.</returns>
	public static AdmissionOutcome Deny(int statusCode, string reason) => new(false, statusCode, reason);
}
