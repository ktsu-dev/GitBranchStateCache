// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Endpoints;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated serialization for the endpoint contracts.
/// </summary>
/// <remarks>
/// Generated rather than reflected so the payload types survive trimming, and so the shape of the
/// wire format is decided in one place rather than by whatever options happen to be in scope at each
/// call site.
/// </remarks>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(StateRequest))]
[JsonSerializable(typeof(StateResponse))]
[JsonSerializable(typeof(BranchesResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class BranchStateJsonContext : JsonSerializerContext
{
}
