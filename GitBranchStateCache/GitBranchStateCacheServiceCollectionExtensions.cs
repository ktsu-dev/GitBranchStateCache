// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache;

using System.IO.Abstractions;
using ktsu.Essentials;
using ktsu.Essentials.FileSystemProviders.Native;
using ktsu.GitBranchStateCache.Admission;
using ktsu.GitBranchStateCache.Coalescing;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Diffs;
using ktsu.GitBranchStateCache.Endpoints;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Observability;
using ktsu.GitBranchStateCache.Readiness;
using ktsu.GitBranchStateCache.Refs;
using ktsu.GitBranchStateCache.Upstreams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Registers the branch state cache with a service collection.
/// </summary>
public static class GitBranchStateCacheServiceCollectionExtensions
{
	/// <summary>
	/// Adds every service the branch state cache needs.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">Configuration to bind the options from.</param>
	/// <returns>The same service collection, for chaining.</returns>
	public static IServiceCollection AddGitBranchStateCache(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		Ensure.NotNull(services);
		Ensure.NotNull(configuration);

		services.AddOptions<GitBranchStateCacheOptions>()
			.Bind(configuration.GetSection(GitBranchStateCacheOptions.SectionName))
			.ValidateOnStart();

		services.AddSingleton<IValidateOptions<GitBranchStateCacheOptions>, GitBranchStateCacheOptionsValidator>();

		services.TryAddTimeProvider();

		// The mirror store depends on IFileSystem rather than the Essentials marker interface, so a
		// mock filesystem can stand in during tests. The Essentials native provider supplies it at
		// runtime.
		services.AddSingleton<IFileSystemProvider, NativeFileSystemProvider>();
		services.AddSingleton<IFileSystem>(provider => provider.GetRequiredService<IFileSystemProvider>());

		services.AddSingleton<IGitRunner, GitRunner>();
		services.AddSingleton<IUpstreamRegistry, UpstreamRegistry>();
		services.AddSingleton<IRepositoryAllowList, RepositoryAllowList>();
		services.AddSingleton<IMirrorStore, MirrorStore>();
		services.AddSingleton<IMirrorQueries, MirrorQueries>();
		services.AddSingleton<IRefResolver, RefResolver>();
		services.AddSingleton<IDiffSource, DiffSource>();
		services.AddSingleton<IDiffCache, DiffCache>();

		// Its own instance, used only for mirror work. The diff cache holds its own, so a repository
		// key and a diff key cannot collide however either is spelled.
		services.AddSingleton<ISingleFlight, SingleFlight>();
		services.AddSingleton<IMirrorFetcher, MirrorFetcher>();
		services.AddSingleton<ICredentialAdmission, CredentialAdmission>();
		services.AddSingleton<IAdmissionGate, AdmissionGate>();

		services.AddSingleton<MirrorReadiness>();
		services.AddSingleton<BranchStateMetrics>();
		services.AddSingleton<BranchStateHandler>();

		services.AddMetrics();

		services.AddSingleton<IHostedService, MirrorStartupCheck>();
		services.AddSingleton<IHostedService, MirrorMaintenanceService>();

		return services;
	}

	private static void TryAddTimeProvider(this IServiceCollection services)
	{
		if (!services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider)))
		{
			services.AddSingleton(TimeProvider.System);
		}
	}
}
