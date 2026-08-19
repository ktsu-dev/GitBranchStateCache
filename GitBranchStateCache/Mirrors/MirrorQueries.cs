// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using Microsoft.Extensions.Options;

/// <summary>
/// Answers commit questions against a mirror.
/// </summary>
/// <param name="runner">Runs git.</param>
/// <param name="options">The configured options.</param>
public sealed class MirrorQueries(IGitRunner runner, IOptions<GitBranchStateCacheOptions> options) : IMirrorQueries
{
	/// <inheritdoc />
	public async Task<bool> ContainsCommitAsync(
		string directory,
		string commit,
		CancellationToken cancellationToken)
	{
		if (!ObjectId.IsValid(commit))
		{
			return false;
		}

		// The suffix makes this ask whether the id is a commit rather than merely an object, so a
		// client naming a blob or a tree is refused instead of failing later inside merge-base.
		GitResult result = await runner.RunAsync(
			new GitInvocation
			{
				WorkingDirectory = directory,
				Arguments = ["cat-file", "-e", $"{commit}^{{commit}}"],
				Timeout = options.Value.ProbeTimeout,
			},
			cancellationToken).ConfigureAwait(false);

		return result.Succeeded;
	}

	/// <inheritdoc />
	public async Task<string?> FindMergeBaseAsync(
		string directory,
		string first,
		string second,
		CancellationToken cancellationToken)
	{
		if (!ObjectId.IsValid(first) || !ObjectId.IsValid(second))
		{
			return null;
		}

		GitResult result = await runner.RunAsync(
			new GitInvocation
			{
				WorkingDirectory = directory,

				// Two ids and nothing else. A merge base is a graph walk over commits and trees, which
				// a blobless mirror has in full, so this never reaches for filtered content.
				Arguments = ["merge-base", first, second],
				Timeout = options.Value.DiffTimeout,
			},
			cancellationToken).ConfigureAwait(false);

		if (!result.Succeeded)
		{
			// Exit code 1 means the histories are unrelated, which is a real answer rather than a
			// failure, and both are handled the same way: there is nothing to diff against.
			return null;
		}

		string merged = result.StandardOutput.Trim();
		return ObjectId.IsValid(merged) ? merged : null;
	}
}
