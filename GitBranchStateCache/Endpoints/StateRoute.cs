// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Endpoints;

/// <summary>
/// Which endpoint a request path names.
/// </summary>
public enum StateRouteKind
{
	/// <summary>Nothing this service serves.</summary>
	Unknown,

	/// <summary>The branch state endpoint.</summary>
	State,

	/// <summary>The branch listing endpoint.</summary>
	Branches,
}

/// <summary>
/// A parsed request path.
/// </summary>
/// <param name="Kind">Which endpoint was addressed.</param>
/// <param name="Upstream">The upstream key.</param>
/// <param name="RepositoryPath">The repository path between the upstream key and the endpoint name.</param>
public sealed record StateRoute(StateRouteKind Kind, string Upstream, string RepositoryPath)
{
	/// <summary>An unrecognised path.</summary>
	public static StateRoute None { get; } = new(StateRouteKind.Unknown, string.Empty, string.Empty);
}
