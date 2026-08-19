// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Git;

/// <summary>
/// Runs one git command.
/// </summary>
/// <remarks>
/// The only unit in this service that starts a process. Everything above it is testable with a fake,
/// which is the point: process management is the least .NET-shaped part of this design and the most
/// likely source of leaks under load, so it is worth having exactly one place where it happens.
/// </remarks>
public interface IGitRunner
{
	/// <summary>
	/// Runs a command to completion, or kills it when it exceeds its timeout or the request is
	/// abandoned.
	/// </summary>
	/// <param name="invocation">What to run.</param>
	/// <param name="cancellationToken">Cancellation token, typically the client's disconnect.</param>
	/// <returns>What the command produced, including a non-zero exit code, which is not an exception.</returns>
	public Task<GitResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken);
}
