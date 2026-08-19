// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using Microsoft.Extensions.Options;

/// <summary>
/// Computes diffs with <c>git diff-tree</c>.
/// </summary>
/// <remarks>
/// The plumbing command rather than <c>git diff</c>, because its output does not change with the
/// caller's configuration. <c>diff.renames</c>, <c>core.quotepath</c>, and the abbreviation length are
/// all things a porcelain command would honour and a plumbing one will not, and every one of them
/// would change the shape of what this parses.
/// <para>
/// The arguments name commit ids and never branch names, which is what stops a fetch landing between
/// two steps of a request from producing an answer torn between two versions of a branch.
/// </para>
/// </remarks>
/// <param name="runner">Runs git.</param>
/// <param name="options">The configured options.</param>
public sealed class DiffSource(IGitRunner runner, IOptions<GitBranchStateCacheOptions> options) : IDiffSource
{
	/// <inheritdoc />
	public async Task<DiffOutcome> ComputeAsync(
		string directory,
		string mergeBase,
		string tip,
		CancellationToken cancellationToken)
	{
		GitResult result = await runner.RunAsync(
			new GitInvocation
			{
				WorkingDirectory = directory,
				Arguments =
				[
					"diff-tree",

					// Recurse into subtrees, so a changed file deep in Content is reported as that file
					// rather than as the top-level directory that contains it.
					"-r",

					// NUL-delimited paths, so nothing is quoted and nothing has to be unquoted.
					"-z",

					// Rename detection turned off deliberately. A rename reported as a delete and an add
					// is exactly what a client wants, because it carries the blob id for both paths, and
					// leaving detection on would make the output depend on a similarity heuristic.
					"--no-renames",
					"--no-commit-id",
					mergeBase,
					tip,
				],
				Timeout = options.Value.DiffTimeout,
			},
			cancellationToken).ConfigureAwait(false);

		if (result.TimedOut)
		{
			return DiffOutcome.Timeout();
		}

		return result.Succeeded
			? DiffOutcome.Success(DiffRawParser.Parse(result.StandardOutput))
			: DiffOutcome.Failed(result.Summary);
	}
}
