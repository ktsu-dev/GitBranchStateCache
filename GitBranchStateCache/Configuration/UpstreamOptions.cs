// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Configuration;

/// <summary>
/// One configured upstream forge.
/// </summary>
public sealed class UpstreamOptions
{
	/// <summary>
	/// Gets or sets the absolute base URL of the upstream, for example <c>https://github.com</c>.
	/// </summary>
	/// <remarks>
	/// Typed as <see cref="Uri"/> rather than a string. The configuration binder converts the
	/// configured string, and a relative or wrong-scheme value still reaches the options validator,
	/// which reports it against the setting name it came from.
	/// </remarks>
	public Uri? BaseUrl { get; set; }

	/// <summary>
	/// Gets the patterns matching the repository paths this upstream may be used for.
	/// </summary>
	/// <remarks>
	/// Required, with no default, no empty-means-everything behaviour, and no acceptable way to spell
	/// "everything". One request for an unlisted repository does not merely warm a cache with content
	/// nobody wanted: it creates a permanent mirror clone, sized by the repository rather than by the
	/// request, on a shared volume, and nothing ever evicts it. There is therefore no legitimate
	/// configuration meaning "clone whatever anyone asks for", and every pattern has to name at least
	/// one literal path segment.
	/// <para>
	/// A pattern matches the repository path exactly, because unlike a Git LFS route the path does not
	/// continue past the repository, so <c>studio/game.git</c> is a complete pattern. <c>*</c> matches
	/// within a segment and <c>**</c> across segments.
	/// </para>
	/// <para>
	/// Getter-only with an initializer because the configuration binder populates in place, and a
	/// settable collection property trips CA2227 under warnings-as-errors.
	/// </para>
	/// </remarks>
	public IList<string> Repositories { get; } = [];
}
