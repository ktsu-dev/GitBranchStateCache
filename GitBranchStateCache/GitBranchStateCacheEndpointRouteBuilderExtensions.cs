// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache;

using ktsu.GitBranchStateCache.Endpoints;
using ktsu.GitBranchStateCache.Readiness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Maps the branch state cache onto an endpoint route builder.
/// </summary>
public static class GitBranchStateCacheEndpointRouteBuilderExtensions
{
	/// <summary>
	/// Maps the health probes and the service's catch-all route.
	/// </summary>
	/// <remarks>
	/// One catch-all route rather than a route per endpoint, because a repository path has variable
	/// depth and ASP.NET routing only allows a catch-all as the final segment, which is where the
	/// endpoint name has to be. The dispatch happens in <see cref="StateRouteParser"/>, which is
	/// directly testable.
	/// </remarks>
	/// <param name="endpoints">The endpoint route builder.</param>
	/// <returns>The same builder, for chaining.</returns>
	public static IEndpointRouteBuilder MapGitBranchStateCache(this IEndpointRouteBuilder endpoints)
	{
		Ensure.NotNull(endpoints);

		endpoints.MapGet("/healthz", () => Results.Text("ok"))
			.WithName("Liveness");

		endpoints.MapGet("/readyz", (MirrorReadiness readiness) => readiness.IsReady
			? Results.Text("ready")
			: Results.Text(
				readiness.FailureReason ?? "The service is not ready.",
				statusCode: StatusCodes.Status503ServiceUnavailable))
			.WithName("Readiness");

		endpoints.MapFallback((HttpContext context, BranchStateHandler handler) => handler.HandleAsync(context))
			.WithName("GitBranchStateCache");

		return endpoints;
	}
}
