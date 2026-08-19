// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Readiness;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated log messages for the startup checks and maintenance.
/// </summary>
internal static partial class ReadinessLog
{
	[LoggerMessage(
		EventId = 3000,
		Level = LogLevel.Information,
		Message = "Started with {GitVersion}, mirroring into '{MirrorRoot}'.")]
	public static partial void GitAvailable(ILogger logger, string gitVersion, string mirrorRoot);

	[LoggerMessage(
		EventId = 3001,
		Level = LogLevel.Information,
		Message = "Mirrors occupy {Bytes} bytes across {Count} repositories.")]
	public static partial void ReportedMirrorSize(ILogger logger, long bytes, int count);

	[LoggerMessage(
		EventId = 3002,
		Level = LogLevel.Warning,
		Message = "The mirror sweep did not finish.")]
	public static partial void SweepFailed(ILogger logger, Exception exception);
}
