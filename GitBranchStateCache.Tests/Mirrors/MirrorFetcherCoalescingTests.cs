// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Mirrors;

using System.Diagnostics.Metrics;
using ktsu.GitBranchStateCache.Coalescing;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Observability;
using ktsu.GitBranchStateCache.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testably.Abstractions.Testing;

/// <summary>
/// The clone or fetch one request starts is shared by every request coalesced onto it, so it must not
/// belong to whichever request happened to arrive first.
/// </summary>
[TestClass]
public class MirrorFetcherCoalescingTests
{
	/// <summary>Gets or sets the context MSTest supplies, used for the run's cancellation token.</summary>
	public TestContext TestContext { get; set; } = null!;

	private static readonly string Root = Path.Combine(
		Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
		"gitbranchstatecache-coalescing");

	private static readonly MirrorKey Key = new("github", "studio/game.git");

	private sealed record Harness(
		MirrorFetcher Fetcher,
		FakeGitRunner Runner,
		FakeHostApplicationLifetime Lifetime,
		string Directory,
		TaskCompletionSource CloneStarted,
		TaskCompletionSource CloneMayFinish);

	/// <summary>
	/// Builds a fetcher whose clone blocks until the test releases it, and which leaves a mirror on the
	/// mock filesystem as a real clone would.
	/// </summary>
	private static Harness Build()
	{
		MockFileSystem fileSystem = new();
		fileSystem.Directory.CreateDirectory(Root);

		FakeTimeProvider time = new(new DateTimeOffset(2026, 8, 19, 9, 47, 0, TimeSpan.Zero));
		IOptions<GitBranchStateCacheOptions> options = Options.Create(new GitBranchStateCacheOptions
		{
			MirrorRoot = Root,
		});

		MirrorStore store = new(fileSystem, options, time);
		Assert.IsTrue(store.TryResolve(Key, out string? directory));

		TaskCompletionSource cloneStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource cloneMayFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeGitRunner runner = BlockingCloneRunner(fileSystem, cloneStarted, cloneMayFinish);

		FakeHostApplicationLifetime lifetime = new();

		MirrorFetcher fetcher = new(
			runner,
			store,
			fileSystem,
			new SingleFlight(),
			new BranchStateMetrics(MeterFactory()),
			options,
			time,
			lifetime,
			NullLogger<MirrorFetcher>.Instance);

		return new Harness(fetcher, runner, lifetime, directory!, cloneStarted, cloneMayFinish);
	}

	private static FakeGitRunner BlockingCloneRunner(
		MockFileSystem fileSystem,
		TaskCompletionSource cloneStarted,
		TaskCompletionSource cloneMayFinish) =>
		new()
		{
			Before = async invocation =>
			{
				if (invocation.Arguments[0] != "clone")
				{
					return;
				}

				cloneStarted.TrySetResult();
				await cloneMayFinish.Task.ConfigureAwait(false);
				fileSystem.Directory.CreateDirectory(fileSystem.Path.Combine(invocation.Arguments[^1], "objects"));
			},
		};

	private static IMeterFactory MeterFactory()
	{
		ServiceCollection services = new();
		services.AddMetrics();
		return services.BuildServiceProvider().GetRequiredService<IMeterFactory>();
	}

	private static Task<MirrorFetchResult> EnsureAsync(Harness harness, CancellationToken cancellationToken) =>
		harness.Fetcher.EnsureCurrentAsync(
			Key,
			harness.Directory,
			new Uri("https://forge.example/studio/game.git"),
			new Uri("https://forge.example"),
			authorization: null,
			cancellationToken);

	[TestMethod]
	public async Task EnsureCurrent_WhenTheLeaderDisconnectsMidClone_FinishesTheCloneForTheFollowerAsync()
	{
		Harness harness = Build();
		using FakeHostApplicationLifetime lifetime = harness.Lifetime;

		using CancellationTokenSource leaderDisconnect = new();
		Task<MirrorFetchResult> leader = EnsureAsync(harness, leaderDisconnect.Token);
		await harness.CloneStarted.Task.WaitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Task<MirrorFetchResult> follower = EnsureAsync(harness, TestContext.CancellationTokenSource.Token);

		await leaderDisconnect.CancelAsync().ConfigureAwait(false);
		await Assert.ThrowsAsync<OperationCanceledException>(() => leader).ConfigureAwait(false);

		harness.CloneMayFinish.TrySetResult();
		MirrorFetchResult result = await follower.ConfigureAwait(false);

		Assert.AreEqual(MirrorFetchStatus.Current, result.Status, result.Failure);
		Assert.AreEqual(1, harness.Runner.CountOf("clone"));
	}

	[TestMethod]
	public async Task EnsureCurrent_WhenTheHostStopsMidClone_CancelsTheCloneAsync()
	{
		Harness harness = Build();
		using FakeHostApplicationLifetime lifetime = harness.Lifetime;

		Task<MirrorFetchResult> leader = EnsureAsync(harness, TestContext.CancellationTokenSource.Token);
		await harness.CloneStarted.Task.WaitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		lifetime.StopApplication();
		harness.CloneMayFinish.TrySetResult();

		// The fake runner throws once its token is cancelled, which is where GitRunner kills the process.
		await Assert.ThrowsAsync<OperationCanceledException>(() => leader).ConfigureAwait(false);
		Assert.AreEqual(0, harness.Runner.CountOf("config"));
	}
}
