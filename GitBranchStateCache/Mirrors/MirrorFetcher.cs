// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

using System.IO.Abstractions;
using ktsu.GitBranchStateCache.Coalescing;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Clones and refreshes mirrors under a requesting client's credential.
/// </summary>
/// <remarks>
/// The mirror is bare and blobless. A partial clone with <c>blob:none</c> fetches commits and trees
/// but no file content, and <c>git diff</c> in its raw form reports blob ids rather than blob
/// contents, so nothing this service runs ever asks for a filtered blob. In a repository using Git
/// LFS the large assets are pointer files of a hundred or so bytes each, so the git object store was
/// never carrying the bulk anyway.
/// <para>
/// A clone or fetch is shared by every request coalesced onto it, so it runs under the service's own
/// lifetime rather than under the request that happened to start it. Each request waits on it with its
/// own disconnect token: a client that hangs up stops waiting, but the work carries on for everyone
/// else. Host shutdown still cancels it, and <see cref="GitBranchStateCacheOptions.FetchTimeout"/>
/// still bounds it, because <see cref="IGitRunner"/> enforces the invocation's timeout itself.
/// </para>
/// <para>
/// Refs a little past <see cref="GitBranchStateCacheOptions.RefsTtl"/> are answered from as they are
/// while one fetch runs in the background, for up to
/// <see cref="GitBranchStateCacheOptions.RefsServeStaleFor"/> and never past
/// <see cref="GitBranchStateCacheOptions.AdmissionTtl"/>. Older refs are fetched before answering.
/// </para>
/// <para>
/// Every clone and fetch is followed by a commit-graph write. A state request finds one merge base per
/// branch, and without a graph each is a walk that parses every commit it passes out of the packs.
/// Failing to write one costs speed, never an answer, so it is not treated as a failure.
/// </para>
/// <para>
/// A clone lands in a temporary directory and is moved into place only once it has finished. A crash
/// part way through a clone of a large repository would otherwise leave a directory that looks like a
/// mirror, and every later request would be answered from a repository missing most of its history.
/// </para>
/// </remarks>
/// <param name="runner">Runs git.</param>
/// <param name="mirrors">Locates mirrors and records their freshness.</param>
/// <param name="fileSystem">The filesystem holding the mirrors.</param>
/// <param name="flights">Keeps concurrent work on one repository to a single clone or fetch.</param>
/// <param name="metrics">Service counters.</param>
/// <param name="options">The configured options.</param>
/// <param name="timeProvider">Clock, injected so freshness is testable.</param>
/// <param name="lifetime">The host's lifetime, whose shutdown cancels a clone or fetch in progress.</param>
/// <param name="logger">Logger.</param>
public sealed class MirrorFetcher(
	IGitRunner runner,
	IMirrorStore mirrors,
	IFileSystem fileSystem,
	ISingleFlight flights,
	BranchStateMetrics metrics,
	IOptions<GitBranchStateCacheOptions> options,
	TimeProvider timeProvider,
	IHostApplicationLifetime lifetime,
	ILogger<MirrorFetcher> logger) : IMirrorFetcher
{
	/// <inheritdoc />
	public async Task<MirrorFetchResult> EnsureCurrentAsync(
		MirrorKey key,
		string directory,
		Uri repositoryUrl,
		Uri upstreamBase,
		string? authorization,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(key);
		Ensure.NotNull(repositoryUrl);

		GitBranchStateCacheOptions settings = options.Value;
		bool exists = mirrors.Exists(directory);
		DateTimeOffset? fetchedAt = exists ? mirrors.RefsFetchedAt(directory) : null;

		if (exists && fetchedAt is DateTimeOffset current)
		{
			TimeSpan age = timeProvider.GetUtcNow() - current;

			if (age < settings.RefsTtl)
			{
				return MirrorFetchResult.Current(current);
			}

			if (age < settings.RefsTtl + StaleWindow(settings))
			{
				RefreshInBackground(key, directory, repositoryUrl, upstreamBase, authorization, fetchedAt);
				return MirrorFetchResult.Current(current);
			}
		}

		IWorkTicket ticket = flights.Acquire(key.ToFlightKey());

		if (!ticket.IsLeader)
		{
			return await FollowAsync(key, directory, ticket, cancellationToken).ConfigureAwait(false);
		}

		// The work owns the leader's ticket from here on, so a leader whose client disconnects stops waiting
		// without abandoning the clone or fetch every follower is waiting on.
		Task<MirrorFetchResult> work = LeadAsync(
			key, directory, repositoryUrl, upstreamBase, authorization, exists, fetchedAt, ticket);

		return await work.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// How long past <see cref="GitBranchStateCacheOptions.RefsTtl"/> refs may be answered from while a
	/// fetch runs in the background.
	/// </summary>
	/// <remarks>
	/// Capped so the refs answered from are never older than
	/// <see cref="GitBranchStateCacheOptions.AdmissionTtl"/>, the same rule the validator holds
	/// <see cref="GitBranchStateCacheOptions.RefsTtl"/> to.
	/// </remarks>
	private static TimeSpan StaleWindow(GitBranchStateCacheOptions settings)
	{
		TimeSpan room = settings.AdmissionTtl - settings.RefsTtl;
		TimeSpan window = settings.RefsServeStaleFor < room ? settings.RefsServeStaleFor : room;
		return window > TimeSpan.Zero ? window : TimeSpan.Zero;
	}

	/// <summary>
	/// Starts a fetch nobody waits on, unless one is already running for this repository.
	/// </summary>
	/// <remarks>
	/// Runs under the requesting caller's credential, which the admission gate has just proven, exactly
	/// as a fetch the request waited on would. A request arriving while it runs is answered from disk
	/// rather than queued behind it.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability",
		"CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed",
		Justification = "The ticket is handed to LeadAsync, which owns it and disposes it when the fetch finishes. Nothing here disposes it.")]
	private void RefreshInBackground(
		MirrorKey key,
		string directory,
		Uri repositoryUrl,
		Uri upstreamBase,
		string? authorization,
		DateTimeOffset? fetchedAt)
	{
		IWorkTicket ticket = flights.Acquire(key.ToFlightKey());

		if (!ticket.IsLeader)
		{
			ticket.Dispose();
			return;
		}

		_ = ObserveAsync(LeadAsync(key, directory, repositoryUrl, upstreamBase, authorization, exists: true, fetchedAt, ticket));
	}

	/// <summary>
	/// Waits on background work so its failure is observed rather than left for the finalizer.
	/// </summary>
	/// <remarks>
	/// A fetch that fails has already recorded and logged it. What reaches here is host shutdown
	/// cancelling it, which is nobody's problem.
	/// </remarks>
	private static async Task ObserveAsync(Task<MirrorFetchResult> work)
	{
		try
		{
			await work.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// The host is stopping.
		}
	}

	/// <summary>
	/// Does the clone or fetch on behalf of every request coalesced onto it, and reports the outcome.
	/// </summary>
	private async Task<MirrorFetchResult> LeadAsync(
		MirrorKey key,
		string directory,
		Uri repositoryUrl,
		Uri upstreamBase,
		string? authorization,
		bool exists,
		DateTimeOffset? fetchedAt,
		IWorkTicket ticket)
	{
		using (ticket)
		{
			CancellationToken stopping = lifetime.ApplicationStopping;

			MirrorFetchResult result = exists
				? await FetchAsync(key, directory, upstreamBase, authorization, fetchedAt, stopping)
					.ConfigureAwait(false)
				: await CloneAsync(key, directory, repositoryUrl, upstreamBase, authorization, stopping)
					.ConfigureAwait(false);

			ticket.Complete(result.Status == MirrorFetchStatus.Current);
			return result;
		}
	}

	/// <summary>
	/// Waits for whichever request is already working on this repository, and releases its ticket.
	/// </summary>
	/// <remarks>
	/// A follower whose leader succeeded re-reads the marker rather than trusting the leader's answer,
	/// because the leader may have been cloning while this request only needed a fetch. A follower
	/// whose leader failed or stalled proceeds against whatever is on disk, which is the whole reason
	/// a stale mirror is a usable one.
	/// </remarks>
	private async Task<MirrorFetchResult> FollowAsync(
		MirrorKey key,
		string directory,
		IWorkTicket ticket,
		CancellationToken cancellationToken)
	{
		metrics.RecordFetchWait(key.Upstream);

		bool succeeded;
		using (ticket)
		{
			succeeded = await ticket
				.WaitForLeaderAsync(options.Value.FetchTimeout, cancellationToken)
				.ConfigureAwait(false);
		}

		if (succeeded && mirrors.RefsFetchedAt(directory) is DateTimeOffset refreshed)
		{
			return MirrorFetchResult.Current(refreshed);
		}

		if (!mirrors.Exists(directory))
		{
			// Nothing on disk and the leader did not manage to create anything. Trying again here
			// would multiply exactly the load the coalescer exists to prevent.
			return MirrorFetchResult.Unavailable(
				"No mirror exists for this repository and the request that was creating one did not finish.");
		}

		return MirrorFetchResult.Stale(
			mirrors.RefsFetchedAt(directory),
			"Another request's fetch did not finish, so these refs may be out of date.");
	}

	private async Task<MirrorFetchResult> CloneAsync(
		MirrorKey key,
		string directory,
		Uri repositoryUrl,
		Uri upstreamBase,
		string? authorization,
		CancellationToken cancellationToken)
	{
		string parent = fileSystem.Path.GetDirectoryName(directory)
			?? throw new InvalidOperationException($"The mirror directory '{directory}' has no parent.");

		fileSystem.Directory.CreateDirectory(parent);

		string staging = fileSystem.Path.Combine(parent, $"{MirrorStore.StagingPrefix}{Guid.NewGuid():N}");

		metrics.RecordClone(key.Upstream);
		MirrorLog.Cloning(logger, key.RepositoryPath, key.Upstream);

		try
		{
			GitResult clone = await runner.RunAsync(
				new GitInvocation
				{
					WorkingDirectory = parent,
					Arguments = ["clone", "--bare", "--filter=blob:none", "--quiet", repositoryUrl.AbsoluteUri, staging],
					CredentialScope = upstreamBase,
					Authorization = authorization,
					Timeout = options.Value.FetchTimeout,
				},
				cancellationToken).ConfigureAwait(false);

			if (!clone.Succeeded)
			{
				metrics.RecordFetchFailure(key.Upstream);
				MirrorLog.CloneFailed(logger, key.RepositoryPath, key.Upstream, clone.Summary);
				return MirrorFetchResult.Unavailable($"Could not clone the repository: {clone.Summary}");
			}

			// A bare clone has no fetch refspec, so without this a later fetch would update nothing and
			// the mirror would be frozen at the moment it was created.
			GitResult configure = await runner.RunAsync(
				new GitInvocation
				{
					WorkingDirectory = staging,
					Arguments = ["config", "remote.origin.fetch", "+refs/heads/*:refs/heads/*"],
					Timeout = options.Value.ProbeTimeout,
				},
				cancellationToken).ConfigureAwait(false);

			if (!configure.Succeeded)
			{
				metrics.RecordFetchFailure(key.Upstream);
				return MirrorFetchResult.Unavailable($"Could not configure the mirror: {configure.Summary}");
			}

			await WriteCommitGraphAsync(key, staging, cancellationToken).ConfigureAwait(false);
			MoveIntoPlace(staging, directory);
		}
		finally
		{
			DiscardStaging(staging);
		}

		mirrors.MarkFetched(directory);
		return MirrorFetchResult.Current(mirrors.RefsFetchedAt(directory) ?? timeProvider.GetUtcNow());
	}

	private async Task<MirrorFetchResult> FetchAsync(
		MirrorKey key,
		string directory,
		Uri upstreamBase,
		string? authorization,
		DateTimeOffset? fetchedAt,
		CancellationToken cancellationToken)
	{
		metrics.RecordFetch(key.Upstream);

		// Recorded before the fetch rather than after it. A fetch of a large repository can run for the
		// whole of FetchTimeout, and a maintenance sweep that ticks during it would otherwise still read
		// the pre-fetch markers, judge the mirror idle, and recursively delete the directory this fetch
		// has open as its working directory.
		mirrors.MarkUsed(directory);

		GitResult fetch = await runner.RunAsync(
			new GitInvocation
			{
				WorkingDirectory = directory,
				Arguments = ["fetch", "--prune", "--quiet", "origin"],
				CredentialScope = upstreamBase,
				Authorization = authorization,
				Timeout = options.Value.FetchTimeout,
			},
			cancellationToken).ConfigureAwait(false);

		if (!fetch.Succeeded)
		{
			metrics.RecordFetchFailure(key.Upstream);
			MirrorLog.FetchFailed(logger, key.RepositoryPath, key.Upstream, fetch.Summary);
			return MirrorFetchResult.Stale(fetchedAt, $"The fetch did not succeed: {fetch.Summary}");
		}

		await WriteCommitGraphAsync(key, directory, cancellationToken).ConfigureAwait(false);

		mirrors.MarkFetched(directory);
		return MirrorFetchResult.Current(mirrors.RefsFetchedAt(directory) ?? timeProvider.GetUtcNow());
	}

	/// <summary>
	/// Brings the mirror's commit-graph up to date with its refs.
	/// </summary>
	/// <remarks>
	/// Split, so after a fetch only the new commits are written as a layer of their own rather than the
	/// whole history again. Bounded by the fetch timeout because it is part of the same work, and its
	/// failure is logged and otherwise ignored, because a mirror without a graph answers the same
	/// questions more slowly.
	/// </remarks>
	private async Task WriteCommitGraphAsync(MirrorKey key, string directory, CancellationToken cancellationToken)
	{
		GitResult graph = await runner.RunAsync(
			new GitInvocation
			{
				WorkingDirectory = directory,
				Arguments = ["commit-graph", "write", "--reachable", "--split", "--no-progress"],
				Timeout = options.Value.FetchTimeout,
			},
			cancellationToken).ConfigureAwait(false);

		if (!graph.Succeeded)
		{
			MirrorLog.CommitGraphNotWritten(logger, key.RepositoryPath, key.Upstream, graph.Summary);
		}
	}

	/// <summary>
	/// Publishes a finished clone.
	/// </summary>
	/// <remarks>
	/// A destination that already exists is treated as success rather than as a conflict. It means
	/// something else finished a clone of the same repository first, and that mirror is every bit as
	/// good as this one.
	/// </remarks>
	private void MoveIntoPlace(string staging, string directory)
	{
		if (mirrors.Exists(directory))
		{
			return;
		}

		try
		{
			fileSystem.Directory.Move(staging, directory);
		}
		catch (IOException) when (mirrors.Exists(directory))
		{
			// Lost the race to another clone of the same repository.
		}
	}

	private void DiscardStaging(string staging)
	{
		try
		{
			if (fileSystem.Directory.Exists(staging))
			{
				fileSystem.Directory.Delete(staging, recursive: true);
			}
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			// A staging directory left behind costs disk until the next sweep, which is better than
			// failing a request that has otherwise already produced a usable mirror.
			MirrorLog.StagingNotRemoved(logger, staging);
		}
	}
}
