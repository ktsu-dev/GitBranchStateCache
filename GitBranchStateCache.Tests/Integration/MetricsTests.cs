// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Integration;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The instruments the README tells operators to watch have to be fed by the code paths they describe.
/// </summary>
[TestClass]
public class MetricsTests
{
	private const string ClientBase = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string MainTip = "cccccccccccccccccccccccccccccccccccccccc";
	private const string ForkPoint = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
	private const string Upstream = "github";
	private const string StateUrl = "/v1/github/studio/game.git/state";

	/// <summary>
	/// Collects measurements from the one service's meters, so tests running alongside it don't leak in.
	/// </summary>
	private sealed class Recorder : IDisposable
	{
		private readonly MeterListener _listener = new();

		public Recorder(IMeterFactory scope)
		{
			_listener.InstrumentPublished = (instrument, listener) =>
			{
				if (ReferenceEquals(instrument.Meter.Scope, scope))
				{
					listener.EnableMeasurementEvents(instrument);
				}
			};
			_listener.SetMeasurementEventCallback<double>(Record);
			_listener.SetMeasurementEventCallback<long>(Record);
			_listener.SetMeasurementEventCallback<int>(Record);
			_listener.Start();
		}

		public ConcurrentQueue<(string Instrument, double Value, string? Upstream)> Measurements { get; } = new();

		public int CountOf(string instrument) =>
			Measurements.Count(measurement => measurement.Instrument == instrument);

		public double SumOf(string instrument) =>
			Measurements.Where(measurement => measurement.Instrument == instrument).Sum(measurement => measurement.Value);

		public IEnumerable<string?> UpstreamsOf(string instrument) =>
			Measurements.Where(measurement => measurement.Instrument == instrument).Select(measurement => measurement.Upstream);

		public void Dispose() => _listener.Dispose();

		private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
			where T : struct
		{
			string? upstream = null;

			foreach (KeyValuePair<string, object?> tag in tags)
			{
				if (tag.Key == "upstream")
				{
					upstream = tag.Value as string;
				}
			}

			Measurements.Enqueue((instrument.Name, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), upstream));
		}
	}

	private static void Seed(ScriptedGit git)
	{
		git.Branches["main"] = MainTip;
		git.Commits.Add(ClientBase);
		git.MergeBases[$"{ClientBase} {MainTip}"] = ForkPoint;
		git.Diffs[$"{ForkPoint} {MainTip}"] =
			":100644 100644 2222222222222222222222222222222222222222 1111111111111111111111111111111111111111 M\0Content/Chars/Bar.uasset\0";
	}

	private static HttpRequestMessage StateRequest() =>
		new(HttpMethod.Post, StateUrl)
		{
			Content = new StringContent(
				$$"""{"base":"{{ClientBase}}","branchPatterns":["origin/main"]}""",
				Encoding.UTF8,
				"application/json"),
			Headers = { Authorization = AuthenticationHeaderValue.Parse("Basic dXNlcjp0b2tlbg==") },
		};

	private static Recorder Listen(ServiceFixture fixture) =>
		new(fixture.Services.GetRequiredService<IMeterFactory>());

	[TestMethod]
	public async Task FetchDuration_IsRecordedForTheCloneAndForEachLaterFetch()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);
		using Recorder recorder = Listen(fixture);

		using (HttpResponseMessage first = await fixture.Client.SendAsync(StateRequest()))
		{
			Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
		}

		Assert.AreEqual(1, fixture.Git.CountOf("clone"));
		Assert.AreEqual(1, recorder.CountOf("gitbranchstatecache.fetch_duration"));

		fixture.Time.Advance(TimeSpan.FromMinutes(1));

		using (HttpResponseMessage second = await fixture.Client.SendAsync(StateRequest()))
		{
			Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
		}

		Assert.AreEqual(1, fixture.Git.CountOf("fetch"));
		Assert.AreEqual(2, recorder.CountOf("gitbranchstatecache.fetch_duration"));
		Assert.IsTrue(recorder.UpstreamsOf("gitbranchstatecache.fetch_duration").All(upstream => upstream == Upstream));
	}

	[TestMethod]
	public async Task FetchDuration_IsRecordedForACloneThatFails()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);
		fixture.Git.CloneFails = true;
		using Recorder recorder = Listen(fixture);

		using HttpResponseMessage response = await fixture.Client.SendAsync(StateRequest());

		Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.AreEqual(1, recorder.CountOf("gitbranchstatecache.fetch_failures"));
		Assert.AreEqual(1, recorder.CountOf("gitbranchstatecache.fetch_duration"));
	}

	[TestMethod]
	public async Task ADiffThatTimesOut_CountsAsACacheMissAsWellAsAFailure()
	{
		// The README's ratio to watch is hits against misses. Leaving the failed diffs out of the misses
		// would leave out the slowest, most expensive ones.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);
		fixture.Git.DiffTimesOut = true;
		using Recorder recorder = Listen(fixture);

		using HttpResponseMessage response = await fixture.Client.SendAsync(StateRequest());

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
		Assert.AreEqual(1.0, recorder.SumOf("gitbranchstatecache.diff_failures"));
		Assert.AreEqual(1.0, recorder.SumOf("gitbranchstatecache.diff_cache_misses"));
		Assert.AreEqual(0.0, recorder.SumOf("gitbranchstatecache.diff_cache_hits"));
	}
}
