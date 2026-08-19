// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Integration;

using System.Collections.Concurrent;
using ktsu.GitBranchStateCache.Git;
using Testably.Abstractions.Testing;

/// <summary>
/// A git runner that answers a whole request's worth of commands from a small model of a repository.
/// </summary>
/// <remarks>
/// Enough of git to drive the real handler end to end without a process or a network, which is what
/// lets the tests that matter assert on ordering and on negatives: that an unlisted repository
/// produced no upstream call, that a refused credential is never served, and that one diff serves
/// many clients.
/// </remarks>
internal sealed class ScriptedGit(MockFileSystem fileSystem) : IGitRunner
{
	private readonly ConcurrentQueue<GitInvocation> _invocations = new();

	/// <summary>Gets every invocation, in the order they were made.</summary>
	public IReadOnlyList<GitInvocation> Invocations => [.. _invocations];

	/// <summary>Gets the branches the mirror holds, as branch name to tip.</summary>
	public Dictionary<string, string> Branches { get; } = new(StringComparer.Ordinal);

	/// <summary>Gets the merge bases, keyed by the two commits joined by a space.</summary>
	public Dictionary<string, string> MergeBases { get; } = new(StringComparer.Ordinal);

	/// <summary>Gets the raw diff output, keyed by merge base and tip joined by a space.</summary>
	public Dictionary<string, string> Diffs { get; } = new(StringComparer.Ordinal);

	/// <summary>Gets the commits the mirror holds.</summary>
	public HashSet<string> Commits { get; } = new(StringComparer.Ordinal);

	/// <summary>Gets or sets whether an ls-remote succeeds.</summary>
	public bool AdmitsCredentials { get; set; } = true;

	/// <summary>Gets or sets whether a diff times out rather than answering.</summary>
	public bool DiffTimesOut { get; set; }

	/// <summary>Gets or sets work to run before a given subcommand answers, for concurrency tests.</summary>
	public Func<GitInvocation, Task>? Before { get; set; }

	/// <summary>Reports how many invocations named a git subcommand.</summary>
	/// <param name="command">The subcommand.</param>
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

		IReadOnlyList<string> arguments = invocation.Arguments;

		return arguments[0] switch
		{
			"--version" => Success("git version 2.50.0"),
			"ls-remote" => AdmitsCredentials
				? Success(string.Empty)
				: new GitResult(128, string.Empty, "fatal: Authentication failed", TimedOut: false),
			"clone" => Clone(arguments),
			"config" => Success(string.Empty),
			"fetch" => Success(string.Empty),
			"for-each-ref" => Success(ForEachRef()),
			"cat-file" => CatFile(arguments),
			"merge-base" => MergeBase(arguments),
			"diff-tree" => Diff(arguments),
			_ => new GitResult(1, string.Empty, $"unscripted command '{arguments[0]}'", TimedOut: false),
		};
	}

	private static GitResult Success(string output) => new(0, output, string.Empty, TimedOut: false);

	private GitResult Clone(IReadOnlyList<string> arguments)
	{
		// The real clone creates the directory it is given, and the fetcher moves it into place only
		// once it has finished, so the model has to create it too.
		fileSystem.Directory.CreateDirectory(arguments[^1]);
		return Success(string.Empty);
	}

	private string ForEachRef() =>
		string.Concat(Branches.Select(branch => $"{branch.Value} refs/heads/{branch.Key}\n"));

	private GitResult CatFile(IReadOnlyList<string> arguments)
	{
		string requested = arguments[^1].Replace("^{commit}", string.Empty, StringComparison.Ordinal);

		return Commits.Contains(requested)
			? Success(string.Empty)
			: new GitResult(1, string.Empty, "fatal: Not a valid object name", TimedOut: false);
	}

	private GitResult MergeBase(IReadOnlyList<string> arguments) =>
		MergeBases.TryGetValue($"{arguments[1]} {arguments[2]}", out string? mergeBase)
			? Success($"{mergeBase}\n")
			: new GitResult(1, string.Empty, string.Empty, TimedOut: false);

	private GitResult Diff(IReadOnlyList<string> arguments)
	{
		if (DiffTimesOut)
		{
			return new GitResult(-1, string.Empty, "timed out", TimedOut: true);
		}

		string key = $"{arguments[^2]} {arguments[^1]}";

		return Diffs.TryGetValue(key, out string? output)
			? Success(output)
			: Success(string.Empty);
	}
}
