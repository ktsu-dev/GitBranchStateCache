// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using Microsoft.Extensions.Options;

/// <summary>
/// Answers commit questions against a mirror.
/// </summary>
/// <remarks>
/// Merge bases are remembered. Both ids are immutable, so an answer is never wrong and never needs
/// invalidating, and every editor's heartbeat asks for the same few hundred of them until someone
/// pulls or a branch moves. Without this, a request naming a few hundred branches starts a few hundred
/// git processes before it can answer anything from the diff cache.
/// </remarks>
/// <param name="runner">Runs git.</param>
/// <param name="options">The configured options.</param>
public sealed class MirrorQueries(IGitRunner runner, IOptions<GitBranchStateCacheOptions> options) : IMirrorQueries
{
	private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> _mergeBases =
		new(StringComparer.Ordinal);

	private readonly LinkedList<KeyValuePair<string, string>> _recency = new();
	private readonly Lock _gate = new();

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

		// The separator cannot appear in a directory or an object id, so no two different triples can
		// produce the same key.
		string cacheKey = $"{directory}\n{first}\n{second}";

		if (TryRead(cacheKey) is string cached)
		{
			return cached;
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

		if (!ObjectId.IsValid(merged))
		{
			return null;
		}

		Publish(cacheKey, merged);
		return merged;
	}

	private string? TryRead(string cacheKey)
	{
		lock (_gate)
		{
			if (!_mergeBases.TryGetValue(cacheKey, out LinkedListNode<KeyValuePair<string, string>>? node))
			{
				return null;
			}

			_recency.Remove(node);
			_recency.AddFirst(node);
			return node.Value.Value;
		}
	}

	private void Publish(string cacheKey, string mergeBase)
	{
		lock (_gate)
		{
			if (_mergeBases.TryGetValue(cacheKey, out LinkedListNode<KeyValuePair<string, string>>? existing))
			{
				_recency.Remove(existing);
			}

			_mergeBases[cacheKey] = _recency.AddFirst(new KeyValuePair<string, string>(cacheKey, mergeBase));

			while (_mergeBases.Count > options.Value.MaxCachedMergeBases && _recency.Last is { } oldest)
			{
				_recency.RemoveLast();
				_mergeBases.Remove(oldest.Value.Key);
			}
		}
	}
}
