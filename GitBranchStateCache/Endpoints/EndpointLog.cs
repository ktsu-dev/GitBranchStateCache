// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Endpoints;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated log messages for the request handler.
/// </summary>
internal static partial class EndpointLog
{
	[LoggerMessage(
		EventId = 2000,
		Level = LogLevel.Warning,
		Message = "Request for unknown upstream '{Upstream}' refused.")]
	public static partial void UnknownUpstream(ILogger logger, string upstream);

	[LoggerMessage(
		EventId = 2001,
		Level = LogLevel.Warning,
		Message = "Request for '{Repository}' refused: no pattern in upstream '{Upstream}' allows it, so no mirror was touched and nothing was asked of the upstream.")]
	public static partial void RepositoryNotAllowed(ILogger logger, string repository, string upstream);

	[LoggerMessage(
		EventId = 2002,
		Level = LogLevel.Information,
		Message = "Base '{Base}' is not a commit the mirror of '{Repository}' holds, so the client is told to fall back to its own computation for this cycle.")]
	public static partial void UnknownBase(ILogger logger, string @base, string repository);

	[LoggerMessage(
		EventId = 2003,
		Level = LogLevel.Warning,
		Message = "Could not answer for branch '{Branch}' of '{Repository}': {Reason}")]
	public static partial void BranchFailed(ILogger logger, string branch, string repository, string reason);

	[LoggerMessage(
		EventId = 2004,
		Level = LogLevel.Error,
		Message = "git produced raw diff output for '{Repository}' that could not be read, so the request was failed rather than answered with a path missing.")]
	public static partial void DiffUnreadable(ILogger logger, Exception exception, string repository);

	[LoggerMessage(
		EventId = 2005,
		Level = LogLevel.Warning,
		Message = "Could not read the refs of the mirror of '{Repository}'.")]
	public static partial void RefsUnreadable(ILogger logger, string repository);

	[LoggerMessage(
		EventId = 2006,
		Level = LogLevel.Warning,
		Message = "Response for '{Repository}' was truncated at {Limit} paths. The client should name the paths it cares about.")]
	public static partial void ResponseTruncated(ILogger logger, string repository, int limit);
}
