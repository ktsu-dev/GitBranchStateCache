// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Mirrors;

using System.IO.Abstractions;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Observability;
using ktsu.GitBranchStateCache.Readiness;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Measures the disk the mirrors occupy and removes the ones nobody asks about any more.
/// </summary>
/// <remarks>
/// The allow-list bounds which repositories may ever be mirrored, which is what makes the volume
/// sizeable in advance, but it does not bound for how long. Without this, an allow-listed repository
/// that stops being queried keeps its mirror forever and disk use only ever ratchets upwards.
/// <para>
/// Deleting a mirror is the cheapest possible way to be wrong: the next request for that repository
/// clones it again. That asymmetry is why the idle limit can be generous and still work.
/// </para>
/// </remarks>
/// <param name="mirrors">Locates mirrors.</param>
/// <param name="fileSystem">The filesystem holding the mirrors.</param>
/// <param name="metrics">Service counters.</param>
/// <param name="options">The configured options.</param>
/// <param name="timeProvider">Clock, injected so the sweep is testable.</param>
/// <param name="logger">Logger.</param>
public sealed class MirrorMaintenanceService(
	IMirrorStore mirrors,
	IFileSystem fileSystem,
	BranchStateMetrics metrics,
	IOptions<GitBranchStateCacheOptions> options,
	TimeProvider timeProvider,
	ILogger<MirrorMaintenanceService> logger) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new(options.Value.MaintenanceInterval, timeProvider);

		do
		{
			try
			{
				Sweep();
			}
			catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
			{
				// A sweep that cannot finish is not a reason to stop sweeping. The next one may find the
				// volume in a better state, and the service is otherwise still answering requests.
				ReadinessLog.SweepFailed(logger, failure);
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
	}

	/// <summary>
	/// How much longer than the longest possible clone a staging directory must be left alone.
	/// </summary>
	/// <remarks>
	/// Covers the time a timed-out git process takes to be killed and the clone's own bookkeeping
	/// either side of it, so that a clone near the end of its timeout is never swept from under it.
	/// </remarks>
	private static readonly TimeSpan StagingMargin = TimeSpan.FromMinutes(10);

	/// <summary>
	/// Removes abandoned clone staging directories, then measures every mirror and reaps the idle ones.
	/// </summary>
	internal void Sweep()
	{
		GitBranchStateCacheOptions settings = options.Value;
		IReadOnlyList<string> directories = mirrors.Enumerate();
		DateTimeOffset now = timeProvider.GetUtcNow();
		long bytes = 0;
		int kept = 0;

		SweepStaging(settings, directories, now);

		foreach (string directory in directories)
		{
			// A mirror can sit inside another when a repository path runs through a mirror.git segment
			// of a repository that is itself mirrored. Deleting the outer one would take the inner one
			// with it, however recently that was used, so it waits until the inner one has gone.
			string[] nested = [.. directories.Where(other => MirrorStore.IsInside(other, directory))];

			if (settings.MirrorIdleMaxAge > TimeSpan.Zero
				&& nested.Length == 0
				&& LastTouched(directory) is DateTimeOffset touched
				&& now - touched > settings.MirrorIdleMaxAge)
			{
				Reap(directory, touched);
				continue;
			}

			bytes += Measure(directory, nested);
			kept++;
		}

		metrics.RecordMirrorBytes(bytes);
		ReadinessLog.ReportedMirrorSize(logger, bytes, kept);
	}

	/// <summary>
	/// Removes the staging directories of clones that can no longer be running.
	/// </summary>
	/// <remarks>
	/// A clone stages into a directory beside its mirror and discards it in a <c>finally</c>, which
	/// never runs when the process is killed partway through: an OOM kill, an eviction or a rollout
	/// during the clone of a large repository. The partial clone, possibly gigabytes, would otherwise
	/// stay on the volume for good, and uncounted, since it is not a mirror.
	/// <para>
	/// A clone runs for at most <see cref="GitBranchStateCacheOptions.FetchTimeout"/> and then
	/// configures the result within <see cref="GitBranchStateCacheOptions.ProbeTimeout"/>, so a staging
	/// directory older than both, plus a margin, belongs to no clone still running. Age is the later of
	/// creation and last write, so a filesystem that cannot report creation times errs towards keeping.
	/// This runs whether or not idle mirrors are reaped, because keeping every mirror is not a reason
	/// to keep what is not one.
	/// </para>
	/// </remarks>
	private void SweepStaging(GitBranchStateCacheOptions settings, IReadOnlyList<string> directories, DateTimeOffset now)
	{
		TimeSpan maxAge = settings.FetchTimeout + settings.ProbeTimeout + StagingMargin;

		foreach (string staging in mirrors.EnumerateStaging())
		{
			// A mirror inside means this is a repository path segment that happens to share the name,
			// not a staging directory.
			if (directories.Any(directory => MirrorStore.IsInside(directory, staging)))
			{
				continue;
			}

			try
			{
				IDirectoryInfo info = fileSystem.DirectoryInfo.New(staging);
				DateTime touched = info.CreationTimeUtc > info.LastWriteTimeUtc
					? info.CreationTimeUtc
					: info.LastWriteTimeUtc;

				if (now - new DateTimeOffset(touched, TimeSpan.Zero) <= maxAge)
				{
					continue;
				}

				mirrors.Delete(staging);
				MirrorLog.SweptStaging(logger, staging);
			}
			catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
			{
				MirrorLog.SweepStagingFailed(logger, failure, staging);
			}
		}
	}

	/// <summary>
	/// Reports when a mirror was last useful to anyone.
	/// </summary>
	/// <remarks>
	/// The later of the last time it answered a request and the last time it was fetched, falling back
	/// to when the directory was created. The fallback matters for a mirror created by a deployment that
	/// predates the markers, which would otherwise look infinitely old and be deleted on the first sweep.
	/// <para>
	/// Both markers are considered rather than the first one present. A request whose refs were already
	/// current returns without fetching and records its own use only once it has been answered, so in
	/// that window the mirror carries a fresh fetch marker alongside a stale use marker. Reading only
	/// the use marker would reap a mirror that is being read from right now.
	/// </para>
	/// </remarks>
	private DateTimeOffset? LastTouched(string directory)
	{
		DateTimeOffset? used = mirrors.LastUsedAt(directory);
		DateTimeOffset? fetched = mirrors.RefsFetchedAt(directory);

		if (used is DateTimeOffset lastUsed && fetched is DateTimeOffset lastFetched)
		{
			return lastUsed > lastFetched ? lastUsed : lastFetched;
		}

		if ((used ?? fetched) is DateTimeOffset recorded)
		{
			return recorded;
		}

		try
		{
			return fileSystem.DirectoryInfo.New(directory).CreationTimeUtc;
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private void Reap(string directory, DateTimeOffset lastUsed)
	{
		try
		{
			mirrors.Delete(directory);
			metrics.RecordMirrorReaped();
			MirrorLog.ReapedIdleMirror(logger, directory, lastUsed);
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			MirrorLog.ReapFailed(logger, failure, directory);
		}
	}

	private long Measure(string directory, string[] nested)
	{
		try
		{
			// Files belonging to a mirror nested inside this one are counted when that mirror is.
			return fileSystem.Directory
				.GetFiles(directory, "*", SearchOption.AllDirectories)
				.Where(file => !nested.Any(inner => MirrorStore.IsInside(file, inner)))
				.Sum(file => fileSystem.FileInfo.New(file).Length);
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			// A mirror being written to while it is measured is normal. Reporting it as zero for one
			// sweep is better than failing the sweep for every other mirror.
			return 0;
		}
	}
}
