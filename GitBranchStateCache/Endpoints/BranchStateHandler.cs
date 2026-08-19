// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Endpoints;

using System.Text.Json;
using ktsu.GitBranchStateCache.Admission;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Diffs;
using ktsu.GitBranchStateCache.Git;
using ktsu.GitBranchStateCache.Mirrors;
using ktsu.GitBranchStateCache.Observability;
using ktsu.GitBranchStateCache.Refs;
using ktsu.GitBranchStateCache.Upstreams;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Answers the branch state and branch listing endpoints.
/// </summary>
/// <remarks>
/// The order of the checks at the top of every request is the safety property, and it is worth
/// stating because reordering any of it changes what this service is:
/// <list type="number">
/// <item>The upstream must be one this deployment is configured for.</item>
/// <item>The repository must be allow-listed. This runs <em>before</em> anything reaches the forge,
/// so an unlisted repository produces no upstream call at all and this service cannot be used as an
/// oracle for which repositories a credential can read.</item>
/// <item>The caller's own credential must be proven against the forge, with no path that admits
/// without a successful upstream call.</item>
/// <item>Only then is a mirror created, fetched, or read.</item>
/// </list>
/// </remarks>
/// <param name="registry">Resolves upstream keys.</param>
/// <param name="allowList">Decides which repositories may be mirrored.</param>
/// <param name="mirrors">Locates mirrors.</param>
/// <param name="fetcher">Creates and refreshes mirrors.</param>
/// <param name="queries">Answers commit questions against a mirror.</param>
/// <param name="refs">Reads a mirror's branches.</param>
/// <param name="diffs">Computes and caches diffs.</param>
/// <param name="admission">Proves credentials against the upstream.</param>
/// <param name="metrics">Service counters.</param>
/// <param name="options">The configured options.</param>
/// <param name="logger">Logger.</param>
internal sealed class BranchStateHandler(
	IUpstreamRegistry registry,
	IRepositoryAllowList allowList,
	IMirrorStore mirrors,
	IMirrorFetcher fetcher,
	IMirrorQueries queries,
	IRefResolver refs,
	IDiffCache diffs,
	IAdmissionGate admission,
	BranchStateMetrics metrics,
	IOptions<GitBranchStateCacheOptions> options,
	ILogger<BranchStateHandler> logger)
{
	/// <summary>
	/// Answers one request.
	/// </summary>
	/// <param name="context">The request.</param>
	/// <returns>What to send back.</returns>
	public async Task<IResult> HandleAsync(HttpContext context)
	{
		Ensure.NotNull(context);

		StateRoute route = StateRouteParser.Parse(context.Request.Path.Value);

		if (route.Kind == StateRouteKind.Unknown)
		{
			return NotFound("no-such-route", "This is not a route this service serves.");
		}

		if (!IsExpectedMethod(route.Kind, context.Request.Method))
		{
			return Failure(
				StatusCodes.Status405MethodNotAllowed,
				"method-not-allowed",
				$"'{route.Kind}' is not addressed with {context.Request.Method}.");
		}

		if (Resolve(route) is not ResolvedRepository repository)
		{
			// One answer for an unknown upstream, an unlisted repository, and a repository path that
			// cannot be laid out on disk. They are different mistakes, and telling them apart would let
			// a caller enumerate what this deployment is configured for.
			return NotFound("no-such-repository", "This service does not serve that repository.");
		}

		string? authorization = context.Request.Headers.Authorization.ToString();
		CancellationToken cancellationToken = context.RequestAborted;

		AdmissionOutcome admitted = await admission.AdmitAsync(
			repository.Key.Upstream,
			repository.Key.RepositoryPath,
			repository.RepositoryUrl,
			repository.UpstreamBase,
			authorization,
			cancellationToken).ConfigureAwait(false);

		if (!admitted.Admitted)
		{
			return Failure(admitted.StatusCode, "not-admitted", admitted.Reason!);
		}

		return route.Kind == StateRouteKind.State
			? await HandleStateAsync(context, repository, authorization, cancellationToken).ConfigureAwait(false)
			: await HandleBranchesAsync(context, repository, authorization, cancellationToken).ConfigureAwait(false);
	}

	private async Task<IResult> HandleStateAsync(
		HttpContext context,
		ResolvedRepository repository,
		string? authorization,
		CancellationToken cancellationToken)
	{
		metrics.RecordStateRequest(repository.Key.Upstream);

		StateRequest? request;

		try
		{
			request = await JsonSerializer
				.DeserializeAsync(context.Request.Body, BranchStateJsonContext.Default.StateRequest, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (JsonException failure)
		{
			return BadRequest($"The request body is not valid JSON: {failure.Message}");
		}

		if (request is null)
		{
			return BadRequest("The request body is empty.");
		}

		if (!ObjectId.IsValid(request.Base))
		{
			return BadRequest(
				"'base' must be a full git object id. Send the latest ancestor of HEAD that has been pushed, "
				+ "which 'git rev-parse @{upstream}' produces without touching the network.");
		}

		if (!TryParsePatterns(request.BranchPatterns, out List<BranchPattern> patterns, out string? patternFailure))
		{
			return BadRequest(patternFailure!);
		}

		int limit = options.Value.MaxPathsPerRequest;

		if (request.Paths is { Count: > 0 } && request.Paths.Count > limit)
		{
			return BadRequest($"'paths' named {request.Paths.Count} paths, and at most {limit} may be sent.");
		}

		MirrorFetchResult mirror = await fetcher.EnsureCurrentAsync(
			repository.Key,
			repository.Directory,
			repository.RepositoryUrl,
			repository.UpstreamBase,
			authorization,
			cancellationToken).ConfigureAwait(false);

		if (mirror.Status == MirrorFetchStatus.Unavailable)
		{
			return Failure(StatusCodes.Status503ServiceUnavailable, "no-mirror", mirror.Failure!);
		}

		mirrors.MarkUsed(repository.Directory);

		if (await refs.ListAsync(repository.Directory, cancellationToken).ConfigureAwait(false)
			is not IReadOnlyList<BranchRef> everyBranch)
		{
			EndpointLog.RefsUnreadable(logger, repository.Key.RepositoryPath);
			return Failure(StatusCodes.Status502BadGateway, "refs-unreadable", "The mirror's refs could not be read.");
		}

		// Read once, into local variables. Everything below names these object ids explicitly and never
		// a branch, so a fetch landing mid-request cannot tear this answer.
		IReadOnlyList<BranchRef> matched = RefResolver.Match(everyBranch, patterns);

		if (!await queries.ContainsCommitAsync(repository.Directory, request.Base!, cancellationToken)
			.ConfigureAwait(false))
		{
			metrics.RecordUnknownBase(repository.Key.Upstream);
			EndpointLog.UnknownBase(logger, request.Base!, repository.Key.RepositoryPath);

			return Failure(
				StatusCodes.Status409Conflict,
				"unknown-base",
				"The mirror does not hold that commit, so no merge base can be computed against it. "
				+ "Fall back to the local computation for this cycle.",
				Summarize(matched));
		}

		try
		{
			return await BuildStateAsync(repository, request, matched, mirror, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (DiffFormatException failure)
		{
			// The one per-branch problem that fails the whole request. Output this service cannot read
			// means it does not know what it is looking at, and answering anything from that position
			// risks reporting a path as unchanged because its record could not be parsed.
			EndpointLog.DiffUnreadable(logger, failure, repository.Key.RepositoryPath);

			return Failure(
				StatusCodes.Status502BadGateway,
				"diff-unreadable",
				"git produced diff output this service could not read, so no answer is given rather than "
				+ "an answer that might be missing a path.");
		}
	}

	private async Task<IResult> BuildStateAsync(
		ResolvedRepository repository,
		StateRequest request,
		IReadOnlyList<BranchRef> branches,
		MirrorFetchResult mirror,
		CancellationToken cancellationToken)
	{
		HashSet<string>? wanted = request.Paths is { Count: > 0 }
			? new HashSet<string>(request.Paths, StringComparer.Ordinal)
			: null;

		Dictionary<string, List<PathChange>> collected = new(StringComparer.Ordinal);
		List<BranchState> answered = [];
		int limit = options.Value.MaxPathsPerRequest;
		bool partial = false;
		bool truncated = false;

		foreach (BranchRef branch in branches)
		{
			string? mergeBase = await queries
				.FindMergeBaseAsync(repository.Directory, request.Base!, branch.Tip, cancellationToken)
				.ConfigureAwait(false);

			if (mergeBase is null)
			{
				partial = true;
				answered.Add(new BranchState(branch.Name, branch.Tip, null, "no-merge-base"));
				EndpointLog.BranchFailed(logger, branch.Name, repository.Key.RepositoryPath, "no merge base");
				continue;
			}

			DiffOutcome diff = await ComputeAsync(repository, mergeBase, branch, cancellationToken)
				.ConfigureAwait(false);

			if (!diff.Succeeded)
			{
				partial = true;
				answered.Add(new BranchState(branch.Name, branch.Tip, mergeBase, diff.Failure));
				EndpointLog.BranchFailed(logger, branch.Name, repository.Key.RepositoryPath, diff.Failure!);
				continue;
			}

			answered.Add(new BranchState(branch.Name, branch.Tip, mergeBase, null));
			truncated |= Collect(diff.Entries!, branch.Name, wanted, limit, collected);
		}

		if (truncated)
		{
			EndpointLog.ResponseTruncated(logger, repository.Key.RepositoryPath, limit);
		}

		metrics.RecordPathsReturned(repository.Key.Upstream, collected.Count);

		return Results.Json(
			new StateResponse(
				request.Base!,
				answered,
				collected.ToDictionary(
					pair => pair.Key,
					pair => (IReadOnlyList<PathChange>)pair.Value,
					StringComparer.Ordinal),
				mirror.RefsAsOf,
				partial,
				truncated),
			BranchStateJsonContext.Default.StateResponse);
	}

	/// <summary>
	/// Computes one branch's diff.
	/// </summary>
	/// <remarks>
	/// Unreadable output is deliberately not caught here. Every other per-branch failure leaves the
	/// client knowing that branch was not answered for, and is reported as such; output this service
	/// cannot read is not scoped to a branch, so it is allowed to reach the caller of this method and
	/// fail the request.
	/// </remarks>
	private Task<DiffOutcome> ComputeAsync(
		ResolvedRepository repository,
		string mergeBase,
		BranchRef branch,
		CancellationToken cancellationToken) =>
		diffs.GetAsync(
			new DiffKey(repository.Key.ToFlightKey(), mergeBase, branch.Tip),
			repository.Directory,
			cancellationToken);

	/// <summary>
	/// Folds one branch's changed paths into the response, and reports whether the limit was reached.
	/// </summary>
	private static bool Collect(
		IReadOnlyList<DiffEntry> entries,
		string branch,
		HashSet<string>? wanted,
		int limit,
		Dictionary<string, List<PathChange>> collected)
	{
		bool truncated = false;

		foreach (DiffEntry entry in entries)
		{
			if (wanted is not null && !wanted.Contains(entry.Path))
			{
				continue;
			}

			if (!collected.TryGetValue(entry.Path, out List<PathChange>? changes))
			{
				if (collected.Count >= limit)
				{
					// Stop naming new paths, but keep going: a path already in the response still wants
					// every branch that carries it, or a client would be told about one branch and not
					// another for the same asset.
					truncated = true;
					continue;
				}

				changes = [];
				collected[entry.Path] = changes;
			}

			changes.Add(new PathChange(branch, entry.Blob, entry.Status.ToString()));
		}

		return truncated;
	}

	private async Task<IResult> HandleBranchesAsync(
		HttpContext context,
		ResolvedRepository repository,
		string? authorization,
		CancellationToken cancellationToken)
	{
		metrics.RecordBranchRequest(repository.Key.Upstream);

		string[] requested =
		[
			.. context.Request.Query["pattern"]
				.Where(pattern => !string.IsNullOrWhiteSpace(pattern))
				.Select(pattern => pattern!)
		];

		if (!TryParsePatterns(requested, out List<BranchPattern> patterns, out string? patternFailure))
		{
			return BadRequest(patternFailure!);
		}

		MirrorFetchResult mirror = await fetcher.EnsureCurrentAsync(
			repository.Key,
			repository.Directory,
			repository.RepositoryUrl,
			repository.UpstreamBase,
			authorization,
			cancellationToken).ConfigureAwait(false);

		if (mirror.Status == MirrorFetchStatus.Unavailable)
		{
			return Failure(StatusCodes.Status503ServiceUnavailable, "no-mirror", mirror.Failure!);
		}

		mirrors.MarkUsed(repository.Directory);

		if (await refs.ListAsync(repository.Directory, cancellationToken).ConfigureAwait(false)
			is not IReadOnlyList<BranchRef> everyBranch)
		{
			EndpointLog.RefsUnreadable(logger, repository.Key.RepositoryPath);
			return Failure(StatusCodes.Status502BadGateway, "refs-unreadable", "The mirror's refs could not be read.");
		}

		return Results.Json(
			new BranchesResponse(Summarize(RefResolver.Match(everyBranch, patterns)), mirror.RefsAsOf),
			BranchStateJsonContext.Default.BranchesResponse);
	}

	/// <summary>
	/// Resolves the upstream, the allow-list, the mirror location, and the upstream URL.
	/// </summary>
	/// <remarks>
	/// Every one of these has to pass before a single byte crosses the network, which is why they are
	/// together in one place rather than spread through the request path.
	/// </remarks>
	private ResolvedRepository? Resolve(StateRoute route)
	{
		if (!registry.TryResolve(route.Upstream, out Uri? upstreamBase))
		{
			EndpointLog.UnknownUpstream(logger, route.Upstream);
			return null;
		}

		if (!allowList.IsAllowed(route.Upstream, route.RepositoryPath))
		{
			EndpointLog.RepositoryNotAllowed(logger, route.RepositoryPath, route.Upstream);
			return null;
		}

		MirrorKey key = new(route.Upstream, route.RepositoryPath);

		if (!mirrors.TryResolve(key, out string? directory)
			|| !UpstreamUrl.TryCombine(upstreamBase!, route.RepositoryPath, out Uri? repositoryUrl))
		{
			return null;
		}

		return new ResolvedRepository(key, directory!, repositoryUrl!, upstreamBase!);
	}

	private static bool TryParsePatterns(
		IReadOnlyList<string>? requested,
		out List<BranchPattern> patterns,
		out string? failure)
	{
		patterns = [];
		failure = null;

		if (requested is null || requested.Count == 0)
		{
			failure = "At least one branch pattern is required.";
			return false;
		}

		foreach (string pattern in requested)
		{
			if (!BranchPattern.TryParse(pattern, out BranchPattern? parsed, out string? reason))
			{
				failure = $"'{pattern}' is not a usable branch pattern: {reason}.";
				return false;
			}

			patterns.Add(parsed!);
		}

		return true;
	}

	private static IReadOnlyList<BranchSummary> Summarize(IReadOnlyList<BranchRef> branches) =>
		[.. branches.Select(branch => new BranchSummary(branch.Name, branch.Tip))];

	private static bool IsExpectedMethod(StateRouteKind kind, string method) => kind switch
	{
		StateRouteKind.State => HttpMethods.IsPost(method),
		StateRouteKind.Branches => HttpMethods.IsGet(method),
		_ => false,
	};

	private static IResult NotFound(string error, string message) =>
		Failure(StatusCodes.Status404NotFound, error, message);

	private static IResult BadRequest(string message) =>
		Failure(StatusCodes.Status400BadRequest, "bad-request", message);

	private static IResult Failure(
		int statusCode,
		string error,
		string message,
		IReadOnlyList<BranchSummary>? branches = null) =>
		Results.Json(
			new ErrorResponse(error, message, branches),
			BranchStateJsonContext.Default.ErrorResponse,
			statusCode: statusCode);
}
