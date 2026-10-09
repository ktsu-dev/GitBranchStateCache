// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Git;

/// <summary>
/// One git command to run.
/// </summary>
/// <remarks>
/// Arguments are passed as a list and never as a joined string, so nothing here is ever parsed by a
/// shell and no argument needs quoting.
/// </remarks>
public sealed class GitInvocation
{
	/// <summary>
	/// Gets the directory to run in, or null to run without one.
	/// </summary>
	/// <remarks>
	/// Null for a clone, which creates the directory it will run in, and set for everything else.
	/// </remarks>
	public string? WorkingDirectory { get; init; }

	/// <summary>Gets the arguments, in order, excluding the executable itself.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>
	/// Gets the URL whose requests the credential applies to, when one is supplied.
	/// </summary>
	/// <remarks>
	/// Git matches HTTP configuration by URL prefix, so scoping the credential to the upstream's base
	/// URL is what stops it being offered to somewhere else if a redirect ever leads off the forge.
	/// </remarks>
	public Uri? CredentialScope { get; init; }

	/// <summary>
	/// Gets the caller's Authorization header, exactly as they sent it, or null for no credential.
	/// </summary>
	/// <remarks>
	/// Never reaches a command line. See <see cref="GitRunner"/> for how it is handed over and why.
	/// </remarks>
	public string? Authorization { get; init; }

	/// <summary>
	/// Gets whether standard output is thrown away unread rather than decoded and returned.
	/// </summary>
	/// <remarks>
	/// For a command whose answer is its exit code alone. Its output is still drained, so the command
	/// never blocks on a full pipe, but it is not held to strict UTF-8: a branch name git allows and
	/// UTF-8 cannot read would otherwise fail a command whose output nobody looks at
	/// (ktsu-dev/GitBranchStateCache#50).
	/// </remarks>
	public bool DiscardStandardOutput { get; init; }

	/// <summary>Gets how long the command may run before it is killed.</summary>
	public required TimeSpan Timeout { get; init; }
}
