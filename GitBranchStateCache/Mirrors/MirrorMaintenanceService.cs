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
	/// Measures every mirror and reaps the idle ones.
	/// </summary>
	internal void Sweep()
	{
		GitBranchStateCacheOptions settings = options.Value;
		IReadOnlyList<string> directories = mirrors.Enumerate();
		DateTimeOffset now = timeProvider.GetUtcNow();
		long bytes = 0;
		int kept = 0;

		foreach (string directory in directories)
		{
			if (settings.MirrorIdleMaxAge > TimeSpan.Zero
				&& LastTouched(directory) is DateTimeOffset touched
				&& now - touched > settings.MirrorIdleMaxAge)
			{
				Reap(directory, touched);
				continue;
			}

			bytes += Measure(directory);
			kept++;
		}

		metrics.RecordMirrorBytes(bytes);
		ReadinessLog.ReportedMirrorSize(logger, bytes, kept);
	}

	/// <summary>
	/// Reports when a mirror was last useful to anyone.
	/// </summary>
	/// <remarks>
	/// The last time it answered a request, falling back to the last fetch and then to when the
	/// directory was created. The fallbacks matter for a mirror created by a deployment that predates
	/// the markers, which would otherwise look infinitely old and be deleted on the first sweep.
	/// </remarks>
	private DateTimeOffset? LastTouched(string directory)
	{
		if (mirrors.LastUsedAt(directory) is DateTimeOffset used)
		{
			return used;
		}

		if (mirrors.RefsFetchedAt(directory) is DateTimeOffset fetched)
		{
			return fetched;
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

	private long Measure(string directory)
	{
		try
		{
			return fileSystem.Directory
				.GetFiles(directory, "*", SearchOption.AllDirectories)
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
