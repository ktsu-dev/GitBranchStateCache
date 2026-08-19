// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Git;

/// <summary>
/// What one git command produced.
/// </summary>
/// <param name="ExitCode">The process exit code, or -1 when it was killed before exiting.</param>
/// <param name="StandardOutput">Everything written to standard output.</param>
/// <param name="StandardError">Everything written to standard error.</param>
/// <param name="TimedOut">Whether the command was killed for exceeding its timeout.</param>
public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
	/// <summary>Gets a value indicating whether the command ran to completion successfully.</summary>
	public bool Succeeded => !TimedOut && ExitCode == 0;

	/// <summary>
	/// Gets standard error trimmed to something worth putting in a log line or a response.
	/// </summary>
	/// <remarks>
	/// Bounded because a failing git command can produce a great deal of output, and an unbounded
	/// copy of it in a response body is a way to make one bad request expensive for everyone.
	/// </remarks>
	public string Summary
	{
		get
		{
			string trimmed = StandardError.Trim();

			if (trimmed.Length == 0)
			{
				trimmed = StandardOutput.Trim();
			}

			return trimmed.Length <= 512 ? trimmed : trimmed[..512];
		}
	}
}
