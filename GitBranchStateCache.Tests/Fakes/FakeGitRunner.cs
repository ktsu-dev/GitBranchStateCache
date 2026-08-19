// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Fakes;

using System.Collections.Concurrent;
using ktsu.GitBranchStateCache.Git;

/// <summary>
/// A git runner that answers from a script and records everything it was asked to run.
/// </summary>
/// <remarks>
/// Everything above <see cref="IGitRunner"/> is meant to be testable without a process, and this is
/// what makes that true. It also records invocations, which is how the tests that matter most here
/// assert a negative: that an unlisted repository produced no upstream call at all.
/// </remarks>
internal sealed class FakeGitRunner : IGitRunner
{
	private readonly ConcurrentQueue<GitInvocation> _invocations = new();

	/// <summary>Gets every invocation, in the order they were made.</summary>
	public IReadOnlyList<GitInvocation> Invocations => [.. _invocations];

	/// <summary>
	/// Gets or sets what to answer with. Defaults to success with no output.
	/// </summary>
	public Func<GitInvocation, GitResult> Respond { get; set; } =
		_ => new GitResult(0, string.Empty, string.Empty, TimedOut: false);

	/// <summary>Gets or sets work to do before answering, for concurrency tests.</summary>
	public Func<GitInvocation, Task>? Before { get; set; }

	/// <summary>Reports how many invocations named a given git subcommand.</summary>
	/// <param name="command">The subcommand, for example <c>ls-remote</c>.</param>
	/// <returns>How many there were.</returns>
	public int CountOf(string command) =>
		_invocations.Count(invocation => invocation.Arguments.Count > 0 && invocation.Arguments[0] == command);

	/// <inheritdoc />
	public async Task<GitResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
	{
		_invocations.Enqueue(invocation);

		if (Before is not null)
		{
			await Before(invocation).ConfigureAwait(false);
		}

		cancellationToken.ThrowIfCancellationRequested();
		return Respond(invocation);
	}
}
