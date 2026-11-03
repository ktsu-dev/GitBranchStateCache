// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Refs;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated log messages for reading refs.
/// </summary>
internal static partial class RefLog
{
	[LoggerMessage(
		EventId = 4000,
		Level = LogLevel.Warning,
		Message = "Skipped a ref in the mirror at '{Directory}' whose name is not valid UTF-8 (bytes {RefNameHex}). Every other branch is still served; rename or delete it upstream to serve it.")]
	public static partial void UndecodableRefSkipped(ILogger logger, string directory, string refNameHex);
}
