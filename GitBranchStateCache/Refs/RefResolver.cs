// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Refs;

using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using Microsoft.Extensions.Options;

/// <summary>
/// Reads branches out of a mirror with <c>for-each-ref</c>.
/// </summary>
/// <remarks>
/// The mirror keeps the upstream's branches at <c>refs/heads</c>, because that is where a bare mirror
/// puts them and rearranging them would buy nothing. Clients do not think in those terms: the plugin
/// gets its branch names from <c>git branch --remotes</c>, which spells them <c>origin/main</c>. The
/// prefix is added here, in one place, so nothing above this has to translate between the two.
/// </remarks>
/// <param name="runner">Runs git.</param>
/// <param name="options">The configured options.</param>
public sealed class RefResolver(IGitRunner runner, IOptions<GitBranchStateCacheOptions> options) : IRefResolver
{
	private const string HeadPrefix = "refs/heads/";

	/// <inheritdoc />
	public async Task<IReadOnlyList<BranchRef>?> ListAsync(string directory, CancellationToken cancellationToken)
	{
		GitResult result = await runner.RunAsync(
			new GitInvocation
			{
				WorkingDirectory = directory,
				Arguments = ["for-each-ref", "--format=%(objectname) %(refname)", HeadPrefix],

				// Reading local refs never touches the network, so the fetch budget would be the wrong
				// bound for it. Anything slower than a probe here means the volume is in trouble.
				Timeout = options.Value.ProbeTimeout,
			},
			cancellationToken).ConfigureAwait(false);

		if (!result.Succeeded)
		{
			return null;
		}

		return Parse(result.StandardOutput, options.Value.RemoteName);
	}

	/// <summary>
	/// Reads <c>for-each-ref</c> output.
	/// </summary>
	/// <remarks>
	/// Split on the first space only. A ref name cannot contain a space, so the first one always
	/// separates the object id from the name, and a line that does not have one is not output this
	/// service produced.
	/// </remarks>
	/// <param name="output">The command output.</param>
	/// <param name="remoteName">The prefix branches are reported under.</param>
	/// <returns>The branches.</returns>
	internal static IReadOnlyList<BranchRef> Parse(string output, string remoteName)
	{
		List<BranchRef> branches = [];

		foreach (string line in output.Split('\n'))
		{
			string trimmed = line.TrimEnd('\r');

			if (trimmed.Length == 0)
			{
				continue;
			}

			int separator = trimmed.IndexOf(' ', StringComparison.Ordinal);

			if (separator <= 0 || !trimmed.AsSpan(separator + 1).StartsWith(HeadPrefix, StringComparison.Ordinal))
			{
				continue;
			}

			string name = trimmed[(separator + 1 + HeadPrefix.Length)..];

			if (name.Length > 0)
			{
				branches.Add(new BranchRef($"{remoteName}/{name}", trimmed[..separator]));
			}
		}

		return branches;
	}

	/// <summary>
	/// Filters branches to those matching any of the requested patterns.
	/// </summary>
	/// <remarks>
	/// Order follows the branch list rather than the pattern list, and a branch matched by two
	/// patterns appears once. A client asking for both its current branch and a wildcard that also
	/// covers it should not be told about the same branch twice.
	/// </remarks>
	/// <param name="branches">Every branch the mirror holds.</param>
	/// <param name="patterns">The patterns to match.</param>
	/// <returns>The matching branches.</returns>
	public static IReadOnlyList<BranchRef> Match(
		IEnumerable<BranchRef> branches,
		IEnumerable<BranchPattern> patterns)
	{
		Ensure.NotNull(branches);
		Ensure.NotNull(patterns);

		BranchPattern[] compiled = [.. patterns];

		return
		[
			.. branches
				.Where(branch => compiled.Any(pattern => pattern.Matches(branch.Name)))
				.DistinctBy(branch => branch.Name, StringComparer.Ordinal)
		];
	}
}
