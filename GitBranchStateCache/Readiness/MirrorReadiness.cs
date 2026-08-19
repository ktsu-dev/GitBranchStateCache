// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Readiness;

/// <summary>
/// Whether the service passed its startup checks, for the readiness probe to report.
/// </summary>
/// <remarks>
/// A flag set once at startup rather than a live probe on every readiness call. Kubernetes polls
/// readiness every few seconds, and re-running the checks that often buys nothing: a volume that goes
/// read-only mid-life shows up as failed fetches, which is degraded behaviour this design already
/// accepts and reports through the refs age in every response.
/// </remarks>
public sealed class MirrorReadiness
{
	/// <summary>Gets a value indicating whether the service is usable.</summary>
	public bool IsReady { get; private set; }

	/// <summary>Gets why the service is not usable, when it is not.</summary>
	public string? FailureReason { get; private set; }

	/// <summary>Records that the startup checks passed.</summary>
	public void MarkReady()
	{
		IsReady = true;
		FailureReason = null;
	}

	/// <summary>Records that a startup check failed.</summary>
	/// <param name="reason">What went wrong, for the probe response and the log.</param>
	public void MarkNotReady(string reason)
	{
		IsReady = false;
		FailureReason = reason;
	}
}
