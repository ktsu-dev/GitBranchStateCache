// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Endpoints;

/// <summary>
/// Splits a request path into an upstream key, a repository path, and an endpoint.
/// </summary>
/// <remarks>
/// Parsed here rather than declared as route templates because a repository path has variable depth
/// and ASP.NET routing only allows a catch-all as the final segment, which is where the endpoint name
/// has to be. Keeping it in a pure function also makes every shape a client can send directly
/// testable without a host.
/// </remarks>
public static class StateRouteParser
{
	/// <summary>The version segment every route begins with.</summary>
	private const string Version = "v1";

	/// <summary>
	/// The fewest segments a valid path can have: the version, an upstream, one repository segment,
	/// and an endpoint name.
	/// </summary>
	private const int MinimumSegments = 4;

	/// <summary>
	/// Parses a request path.
	/// </summary>
	/// <param name="path">The request path, already percent-decoded.</param>
	/// <returns>What the path names, or <see cref="StateRoute.None"/> when it names nothing.</returns>
	public static StateRoute Parse(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return StateRoute.None;
		}

		string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (segments.Length < MinimumSegments || !string.Equals(segments[0], Version, StringComparison.Ordinal))
		{
			return StateRoute.None;
		}

		StateRouteKind kind = segments[^1] switch
		{
			"state" => StateRouteKind.State,
			"branches" => StateRouteKind.Branches,
			_ => StateRouteKind.Unknown,
		};

		if (kind == StateRouteKind.Unknown)
		{
			return StateRoute.None;
		}

		return new StateRoute(kind, segments[1], string.Join('/', segments[2..^1]));
	}
}
