// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Integration;

using System.IO.Abstractions;
using ktsu.GitBranchStateCache.Git;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Testably.Abstractions.Testing;

/// <summary>
/// Builds a real in-process service over a mock filesystem and a scripted git.
/// </summary>
/// <remarks>
/// Assembled through its own <c>AddGitBranchStateCache</c> and <c>MapGitBranchStateCache</c>
/// extensions rather than by hand, so these tests exercise the wiring a host actually gets, including
/// the startup check and the options validator. Exactly three things are swapped: the filesystem, so
/// no test touches a disk, git, so no test starts a process or reaches a network, and the clock, so
/// freshness is decided rather than waited for.
/// </remarks>
internal sealed class ServiceFixture : IAsyncDisposable
{
	private readonly IHost _host;

	private ServiceFixture(IHost host, ScriptedGit git, MockFileSystem fileSystem, FakeTimeProvider time)
	{
		_host = host;
		Git = git;
		FileSystem = fileSystem;
		Time = time;
	}

	/// <summary>Gets the scripted git the service runs.</summary>
	public ScriptedGit Git { get; }

	/// <summary>Gets the in-memory filesystem the mirrors are written to.</summary>
	public MockFileSystem FileSystem { get; }

	/// <summary>Gets the clock the service reads.</summary>
	public FakeTimeProvider Time { get; }

	/// <summary>Gets a client addressed at the service.</summary>
	public HttpClient Client => _host.GetTestClient();

	/// <summary>Gets the absolute mirror root, valid on whichever platform the suite runs on.</summary>
	public static string MirrorRoot { get; } = Path.Combine(
		Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
		"gitbranchstatecache-integration");

	/// <summary>
	/// Starts a service.
	/// </summary>
	/// <param name="settings">Extra configuration values, overriding the defaults.</param>
	/// <returns>The running fixture.</returns>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Assembling a host is coupled to the host, routing, configuration and service types by nature. Splitting it up would scatter the wiring these tests exist to exercise.")]
	public static async Task<ServiceFixture> StartAsync(Dictionary<string, string?>? settings = null)
	{
		MockFileSystem fileSystem = new();
		fileSystem.Directory.CreateDirectory(MirrorRoot);

		ScriptedGit git = new(fileSystem);
		FakeTimeProvider time = new(new DateTimeOffset(2026, 8, 19, 9, 47, 0, TimeSpan.Zero));

		Dictionary<string, string?> configuration = new(StringComparer.Ordinal)
		{
			["GitBranchStateCache:MirrorRoot"] = MirrorRoot,
			["GitBranchStateCache:RefsTtl"] = "00:00:30",
			["GitBranchStateCache:AdmissionTtl"] = "00:01:00",
			["GitBranchStateCache:FetchTimeout"] = "00:00:05",
			["GitBranchStateCache:DiffTimeout"] = "00:00:05",
			["GitBranchStateCache:ProbeTimeout"] = "00:00:05",
			["GitBranchStateCache:MaintenanceInterval"] = "01:00:00",
			["GitBranchStateCache:Upstreams:github:BaseUrl"] = "https://github.example",
			["GitBranchStateCache:Upstreams:github:Repositories:0"] = "studio/game.git",
		};

		foreach ((string key, string? value) in settings ?? [])
		{
			configuration[key] = value;
		}

		IHost host = await new HostBuilder()
			.ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configuration))
			.ConfigureWebHost(webHost => webHost
				.UseTestServer()
				.ConfigureServices((context, services) =>
				{
					services.AddRouting();
					services.AddSingleton<System.TimeProvider>(time);
					services.AddGitBranchStateCache(context.Configuration);

					services.RemoveAll<IFileSystem>();
					services.AddSingleton<IFileSystem>(fileSystem);

					services.RemoveAll<IGitRunner>();
					services.AddSingleton<IGitRunner>(git);
				})
				.Configure(app =>
				{
					app.UseRouting();
					app.UseEndpoints(endpoints => endpoints.MapGitBranchStateCache());
				}))
			.StartAsync();

		return new ServiceFixture(host, git, fileSystem, time);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		await _host.StopAsync();
		_host.Dispose();
	}
}
