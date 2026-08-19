// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Readiness;

using System.IO.Abstractions;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Refuses to start when the mirror root is not writable or git is not usable.
/// </summary>
/// <remarks>
/// Failing here rather than on the first request is deliberate. A service whose volume is missing, or
/// whose image is missing the git binary it shells out to, looks healthy from the outside and then
/// fails every request, which is a worse outcome than a pod that will not start and says why.
/// <para>
/// The git check runs a real invocation rather than looking for a file on the path, because what
/// matters is not that something called git exists but that this process can start it and read what
/// it wrote.
/// </para>
/// </remarks>
/// <param name="fileSystem">The filesystem holding the mirrors.</param>
/// <param name="runner">Runs git.</param>
/// <param name="readiness">The flag the readiness probe reports.</param>
/// <param name="options">The configured options.</param>
/// <param name="logger">Logger.</param>
public sealed class MirrorStartupCheck(
	IFileSystem fileSystem,
	IGitRunner runner,
	MirrorReadiness readiness,
	IOptions<GitBranchStateCacheOptions> options,
	ILogger<MirrorStartupCheck> logger) : IHostedService
{
	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		GitBranchStateCacheOptions settings = options.Value;

		await EnsureRootWritableAsync(settings, cancellationToken).ConfigureAwait(false);
		await EnsureGitUsableAsync(settings, cancellationToken).ConfigureAwait(false);

		readiness.MarkReady();
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	private async Task EnsureRootWritableAsync(
		GitBranchStateCacheOptions settings,
		CancellationToken cancellationToken)
	{
		string root = settings.MirrorRoot;

		try
		{
			fileSystem.Directory.CreateDirectory(root);

			string probe = fileSystem.Path.Combine(root, $".writable-{Guid.NewGuid():N}");
			await fileSystem.File.WriteAllTextAsync(probe, string.Empty, cancellationToken).ConfigureAwait(false);
			fileSystem.File.Delete(probe);

			// Every git invocation is pointed at this file as its global configuration, so that nothing
			// configured for the account this process runs as can reach a run. Created empty here so
			// its absence is never mistaken for a misconfiguration.
			string isolatedConfig = GitRunner.GlobalConfigPath(settings);

			if (!fileSystem.File.Exists(isolatedConfig))
			{
				await fileSystem.File.WriteAllTextAsync(isolatedConfig, string.Empty, cancellationToken)
					.ConfigureAwait(false);
			}
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException
			or NotSupportedException or ArgumentException)
		{
			readiness.MarkNotReady($"The mirror root '{root}' is not writable: {failure.Message}");

			throw new InvalidOperationException(
				$"The configured mirror root '{root}' is not writable. Check the volume mount and its permissions.",
				failure);
		}
	}

	private async Task EnsureGitUsableAsync(
		GitBranchStateCacheOptions settings,
		CancellationToken cancellationToken)
	{
		GitResult version;

		try
		{
			version = await runner.RunAsync(
				new GitInvocation
				{
					Arguments = ["--version"],
					Timeout = settings.ProbeTimeout,
				},
				cancellationToken).ConfigureAwait(false);
		}
		catch (Exception failure) when (failure is System.ComponentModel.Win32Exception
			or InvalidOperationException or System.IO.FileNotFoundException)
		{
			readiness.MarkNotReady($"The configured git executable '{settings.GitExecutable}' could not be started.");

			throw new InvalidOperationException(
				$"The configured git executable '{settings.GitExecutable}' could not be started. This service shells out to git for everything it does, so it cannot run without one.",
				failure);
		}

		if (!version.Succeeded)
		{
			readiness.MarkNotReady($"'{settings.GitExecutable} --version' failed: {version.Summary}");

			throw new InvalidOperationException(
				$"'{settings.GitExecutable} --version' failed: {version.Summary}");
		}

		string reported = version.StandardOutput.Trim();
		ReadinessLog.GitAvailable(logger, reported, settings.MirrorRoot);
	}
}
