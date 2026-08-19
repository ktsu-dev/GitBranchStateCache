// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Observability;

using System.Diagnostics.Metrics;

/// <summary>
/// Counters describing how well the service is doing its job.
/// </summary>
/// <remarks>
/// Built on <see cref="Meter"/> with no exporter dependency. Adding an OpenTelemetry exporter is a few
/// lines in the host, and pinning one here would be a guess about the scraping setup.
/// <para>
/// The pair worth watching is diff cache hits against misses. It reflects how well merge bases are
/// clustering: a whole team working off one integration point shares one merge base and therefore one
/// computed diff per branch, so a low ratio means the team is spread across many integration points
/// and the cache bound may need raising.
/// </para>
/// <para>
/// The one to alert on is unknown-base responses. Every one of them is a client falling back to the
/// local computation this service exists to replace, and a rising count means the assumption that
/// clients sit near a shared integration point is failing.
/// </para>
/// </remarks>
public sealed class BranchStateMetrics : IDisposable
{
	/// <summary>The meter name to subscribe to when exporting these counters.</summary>
	public const string MeterName = "ktsu.GitBranchStateCache";

	private const string RequestUnit = "{request}";
	private const string DiffUnit = "{diff}";

	private readonly Meter _meter;
	private readonly Counter<long> _stateRequests;
	private readonly Counter<long> _branchRequests;
	private readonly Counter<long> _unknownBase;
	private readonly Counter<long> _diffCacheHits;
	private readonly Counter<long> _diffCacheMisses;
	private readonly Counter<long> _diffFailures;
	private readonly Counter<long> _clones;
	private readonly Counter<long> _fetches;
	private readonly Counter<long> _fetchFailures;
	private readonly Counter<long> _fetchWaits;
	private readonly Counter<long> _admissionProbes;
	private readonly Counter<long> _admissionRejections;
	private readonly Counter<long> _mirrorsReaped;
	private readonly Histogram<double> _fetchDuration;
	private readonly Histogram<double> _diffDuration;
	private readonly Histogram<int> _pathsReturned;

	private long _mirrorBytes;

	/// <summary>
	/// Initializes a new instance of the <see cref="BranchStateMetrics"/> class.
	/// </summary>
	/// <param name="meterFactory">The factory the host supplies.</param>
	public BranchStateMetrics(IMeterFactory meterFactory)
	{
		Ensure.NotNull(meterFactory);

		_meter = meterFactory.Create(MeterName);
		_stateRequests = _meter.CreateCounter<long>("gitbranchstatecache.state_requests", unit: RequestUnit, description: "Branch state requests answered.");
		_branchRequests = _meter.CreateCounter<long>("gitbranchstatecache.branch_requests", unit: RequestUnit, description: "Branch listing requests answered.");
		_unknownBase = _meter.CreateCounter<long>("gitbranchstatecache.unknown_base", unit: RequestUnit, description: "Requests refused because the mirror does not contain the base commit the client named.");
		_diffCacheHits = _meter.CreateCounter<long>("gitbranchstatecache.diff_cache_hits", unit: DiffUnit, description: "Diffs answered from the cache because another client shares this merge base.");
		_diffCacheMisses = _meter.CreateCounter<long>("gitbranchstatecache.diff_cache_misses", unit: DiffUnit, description: "Diffs that had to be computed.");
		_diffFailures = _meter.CreateCounter<long>("gitbranchstatecache.diff_failures", unit: DiffUnit, description: "Diffs that failed or timed out, leaving their branch unanswered.");
		_clones = _meter.CreateCounter<long>("gitbranchstatecache.clones", unit: "{clone}", description: "Mirrors created, each of which is a permanent addition to the volume.");
		_fetches = _meter.CreateCounter<long>("gitbranchstatecache.fetches", unit: "{fetch}", description: "Incremental fetches performed against an upstream.");
		_fetchFailures = _meter.CreateCounter<long>("gitbranchstatecache.fetch_failures", unit: "{fetch}", description: "Clones or fetches that did not succeed.");
		_fetchWaits = _meter.CreateCounter<long>("gitbranchstatecache.fetch_waits", unit: RequestUnit, description: "Requests that waited for another request's fetch instead of fetching themselves.");
		_admissionProbes = _meter.CreateCounter<long>("gitbranchstatecache.admission_probes", unit: RequestUnit, description: "ls-remote calls made only to prove a credential may read a repository.");
		_admissionRejections = _meter.CreateCounter<long>("gitbranchstatecache.admission_rejections", unit: RequestUnit, description: "Admission probes the upstream refused.");
		_mirrorsReaped = _meter.CreateCounter<long>("gitbranchstatecache.mirrors_reaped", unit: "{mirror}", description: "Mirrors deleted for going unqueried for longer than the idle limit.");
		_fetchDuration = _meter.CreateHistogram<double>("gitbranchstatecache.fetch_duration", unit: "s", description: "How long clones and fetches took.");
		_diffDuration = _meter.CreateHistogram<double>("gitbranchstatecache.diff_duration", unit: "s", description: "How long computing one diff took.");
		_pathsReturned = _meter.CreateHistogram<int>("gitbranchstatecache.paths_returned", unit: "{path}", description: "How many changed paths each response carried.");

		_meter.CreateObservableGauge(
			"gitbranchstatecache.mirror_bytes",
			() => Interlocked.Read(ref _mirrorBytes),
			unit: "By",
			description: "Bytes the mirrors occupy on disk, as of the last sweep.");
	}

	/// <summary>Records a branch state request.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordStateRequest(string upstream) => _stateRequests.Add(1, Tag(upstream));

	/// <summary>Records a branch listing request.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordBranchRequest(string upstream) => _branchRequests.Add(1, Tag(upstream));

	/// <summary>Records a request whose base commit the mirror does not contain.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordUnknownBase(string upstream) => _unknownBase.Add(1, Tag(upstream));

	/// <summary>Records a diff answered from the cache.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordDiffCacheHit(string upstream) => _diffCacheHits.Add(1, Tag(upstream));

	/// <summary>Records a diff that had to be computed, and how long it took.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	/// <param name="elapsed">How long the computation took.</param>
	public void RecordDiffComputed(string upstream, TimeSpan elapsed)
	{
		_diffCacheMisses.Add(1, Tag(upstream));
		_diffDuration.Record(elapsed.TotalSeconds, Tag(upstream));
	}

	/// <summary>Records a diff that could not be produced.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordDiffFailure(string upstream) => _diffFailures.Add(1, Tag(upstream));

	/// <summary>Records a mirror being created.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordClone(string upstream) => _clones.Add(1, Tag(upstream));

	/// <summary>Records an incremental fetch.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordFetch(string upstream) => _fetches.Add(1, Tag(upstream));

	/// <summary>Records a clone or fetch that did not succeed.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordFetchFailure(string upstream) => _fetchFailures.Add(1, Tag(upstream));

	/// <summary>Records a request that waited for another request's fetch.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordFetchWait(string upstream) => _fetchWaits.Add(1, Tag(upstream));

	/// <summary>Records how long a clone or fetch took.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	/// <param name="elapsed">How long it took.</param>
	public void RecordFetchDuration(string upstream, TimeSpan elapsed) =>
		_fetchDuration.Record(elapsed.TotalSeconds, Tag(upstream));

	/// <summary>Records an ls-remote made only to prove a credential.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordAdmissionProbe(string upstream) => _admissionProbes.Add(1, Tag(upstream));

	/// <summary>Records an admission probe the upstream refused.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	public void RecordAdmissionRejected(string upstream) => _admissionRejections.Add(1, Tag(upstream));

	/// <summary>Records a mirror deleted for being idle.</summary>
	public void RecordMirrorReaped() => _mirrorsReaped.Add(1);

	/// <summary>Records how many changed paths a response carried.</summary>
	/// <param name="upstream">The upstream key, recorded as a tag.</param>
	/// <param name="paths">How many paths.</param>
	public void RecordPathsReturned(string upstream, int paths) => _pathsReturned.Record(paths, Tag(upstream));

	/// <summary>Records the disk the mirrors occupy, as measured by the last sweep.</summary>
	/// <param name="bytes">How many bytes.</param>
	public void RecordMirrorBytes(long bytes) => Interlocked.Exchange(ref _mirrorBytes, bytes);

	/// <inheritdoc />
	public void Dispose() => _meter.Dispose();

	private static KeyValuePair<string, object?> Tag(string upstream) => new("upstream", upstream);
}
