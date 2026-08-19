// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated log messages for the mirror subsystem.
/// </summary>
internal static partial class MirrorLog
{
	[LoggerMessage(
		EventId = 1000,
		Level = LogLevel.Information,
		Message = "Cloning a bare blobless mirror of '{Repository}' from upstream '{Upstream}'. The first request for a repository pays for this.")]
	public static partial void Cloning(ILogger logger, string repository, string upstream);

	[LoggerMessage(
		EventId = 1001,
		Level = LogLevel.Warning,
		Message = "Could not clone '{Repository}' from upstream '{Upstream}': {Reason}")]
	public static partial void CloneFailed(ILogger logger, string repository, string upstream, string reason);

	[LoggerMessage(
		EventId = 1002,
		Level = LogLevel.Warning,
		Message = "Could not fetch '{Repository}' from upstream '{Upstream}', so its existing refs are served and reported as older: {Reason}")]
	public static partial void FetchFailed(ILogger logger, string repository, string upstream, string reason);

	[LoggerMessage(
		EventId = 1003,
		Level = LogLevel.Warning,
		Message = "Could not remove the staging directory '{Staging}'. It holds disk until the next sweep.")]
	public static partial void StagingNotRemoved(ILogger logger, string staging);

	[LoggerMessage(
		EventId = 1004,
		Level = LogLevel.Information,
		Message = "Removed the mirror at '{Directory}', which has not answered a request since {LastUsed}.")]
	public static partial void ReapedIdleMirror(ILogger logger, string directory, DateTimeOffset lastUsed);

	[LoggerMessage(
		EventId = 1005,
		Level = LogLevel.Warning,
		Message = "Could not remove the idle mirror at '{Directory}'.")]
	public static partial void ReapFailed(ILogger logger, Exception exception, string directory);
}
