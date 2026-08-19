// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

using System.Diagnostics;
using ktsu.GitBranchStateCache.Coalescing;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Observability;
using Microsoft.Extensions.Options;

/// <summary>
/// An in-memory, least-recently-used cache of computed diffs.
/// </summary>
/// <remarks>
/// Keys are immutable commit ids, so an entry is never wrong and never needs invalidating. Bounded by
/// entry count rather than by memory, because the thing worth bounding is how many merge bases are
/// tracked at once and the size of any one diff is a property of the repository rather than of this
/// service.
/// <para>
/// Concurrent misses for the same key are coalesced. A whole team polling on the same thirty second
/// heartbeat and sharing one integration point is the expected load, so the first request after an
/// integration lands would otherwise start the same expensive computation once per client.
/// </para>
/// </remarks>
/// <param name="source">Computes a diff on a miss.</param>
/// <param name="metrics">Service counters.</param>
/// <param name="options">The configured options.</param>
public sealed class DiffCache(
	IDiffSource source,
	BranchStateMetrics metrics,
	IOptions<GitBranchStateCacheOptions> options) : IDiffCache
{
	/// <summary>
	/// Its own coalescer rather than the one the mirror fetcher uses, so a diff key and a repository
	/// key cannot collide however either is spelled.
	/// </summary>
	private readonly SingleFlight _flights = new();

	private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
	private readonly LinkedList<Entry> _recency = new();
	private readonly Lock _gate = new();

	/// <inheritdoc />
	public async Task<DiffOutcome> GetAsync(DiffKey key, string directory, CancellationToken cancellationToken)
	{
		Ensure.NotNull(key);

		string cacheKey = ToCacheKey(key);
		string upstream = UpstreamOf(key);

		if (TryRead(cacheKey) is IReadOnlyList<DiffEntry> cached)
		{
			metrics.RecordDiffCacheHit(upstream);
			return DiffOutcome.Success(cached);
		}

		using IWorkTicket ticket = _flights.Acquire(cacheKey);

		if (!ticket.IsLeader)
		{
			bool succeeded = await ticket
				.WaitForLeaderAsync(options.Value.DiffTimeout, cancellationToken)
				.ConfigureAwait(false);

			if (succeeded && TryRead(cacheKey) is IReadOnlyList<DiffEntry> published)
			{
				metrics.RecordDiffCacheHit(upstream);
				return DiffOutcome.Success(published);
			}

			// The leader failed or stalled. Computing it here as well is the correct fallback: a
			// follower that gives up would report a branch as failed that nothing has actually tried
			// to answer for it.
			return await ComputeAsync(cacheKey, upstream, directory, key, cancellationToken)
				.ConfigureAwait(false);
		}

		DiffOutcome outcome = await ComputeAsync(cacheKey, upstream, directory, key, cancellationToken)
			.ConfigureAwait(false);

		ticket.Complete(outcome.Succeeded);
		return outcome;
	}

	private async Task<DiffOutcome> ComputeAsync(
		string cacheKey,
		string upstream,
		string directory,
		DiffKey key,
		CancellationToken cancellationToken)
	{
		long started = Stopwatch.GetTimestamp();

		DiffOutcome outcome = await source
			.ComputeAsync(directory, key.MergeBase, key.Tip, cancellationToken)
			.ConfigureAwait(false);

		if (!outcome.Succeeded)
		{
			metrics.RecordDiffFailure(upstream);
			return outcome;
		}

		metrics.RecordDiffComputed(upstream, Stopwatch.GetElapsedTime(started));
		Publish(cacheKey, outcome.Entries!);
		return outcome;
	}

	private IReadOnlyList<DiffEntry>? TryRead(string cacheKey)
	{
		lock (_gate)
		{
			if (!_entries.TryGetValue(cacheKey, out LinkedListNode<Entry>? node))
			{
				return null;
			}

			_recency.Remove(node);
			_recency.AddFirst(node);
			return node.Value.Paths;
		}
	}

	private void Publish(string cacheKey, IReadOnlyList<DiffEntry> paths)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(cacheKey, out LinkedListNode<Entry>? existing))
			{
				_recency.Remove(existing);
				_entries.Remove(cacheKey);
			}

			_entries[cacheKey] = _recency.AddFirst(new Entry(cacheKey, paths));

			while (_entries.Count > options.Value.MaxCachedDiffs && _recency.Last is LinkedListNode<Entry> oldest)
			{
				_recency.RemoveLast();
				_entries.Remove(oldest.Value.Key);
			}
		}
	}

	/// <summary>
	/// Renders a cache key.
	/// </summary>
	/// <remarks>
	/// The separator cannot appear in a repository path or an object id, so no two different triples
	/// can produce the same string.
	/// </remarks>
	private static string ToCacheKey(DiffKey key) => $"{key.Repository}\n{key.MergeBase}\n{key.Tip}";

	/// <summary>
	/// Recovers the upstream key from a repository identifier for metric tagging.
	/// </summary>
	/// <remarks>
	/// The repository identifier is the upstream key and the path joined, so the part before the first
	/// separator is the upstream. Only used as a metric tag, so an unexpected shape degrades to an
	/// unhelpful tag rather than to an error.
	/// </remarks>
	private static string UpstreamOf(DiffKey key)
	{
		int separator = key.Repository.IndexOf('\n', StringComparison.Ordinal);
		return separator > 0 ? key.Repository[..separator] : key.Repository;
	}

	private sealed record Entry(string Key, IReadOnlyList<DiffEntry> Paths);
}
