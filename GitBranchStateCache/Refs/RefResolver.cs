// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Refs;

using System.Text;
using ktsu.GitBranchStateCache.Configuration;
using ktsu.GitBranchStateCache.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// <param name="logger">Where a skipped ref is reported, or null to report nothing.</param>
public sealed class RefResolver(
	IGitRunner runner,
	IOptions<GitBranchStateCacheOptions> options,
	ILogger<RefResolver>? logger = null) : IRefResolver
{
	private const string HeadPrefix = "refs/heads/";

	private static readonly Encoding StrictUtf8 = new UTF8Encoding(
		encoderShouldEmitUTF8Identifier: false,
		throwOnInvalidBytes: true);

	private readonly ILogger _logger = logger ?? NullLogger<RefResolver>.Instance;

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

				// git does not require ref names to be UTF-8, and one that is not must not take every
				// other branch down with it. Each line is decoded on its own below.
				ReadStandardOutputAsBytes = true,
			},
			cancellationToken).ConfigureAwait(false);

		if (!result.Succeeded)
		{
			return null;
		}

		string decoded = DecodeLines(
			result.StandardOutput,
			undecodable => RefLog.UndecodableRefSkipped(_logger, directory, undecodable));

		return Parse(decoded, options.Value.RemoteName);
	}

	/// <summary>
	/// Decodes <c>for-each-ref</c> output read as bytes, one line at a time, dropping any line that is
	/// not UTF-8.
	/// </summary>
	/// <remarks>
	/// A name that cannot be decoded cannot be written into a response faithfully either, so the ref
	/// is skipped rather than served under a name with a replacement character in it, which would look
	/// fine and match nothing the client has.
	/// </remarks>
	/// <param name="output">The output, one character per byte.</param>
	/// <param name="skipped">Told the hex of each ref name that was dropped.</param>
	/// <returns>The lines that decoded, each followed by a line feed.</returns>
	internal static string DecodeLines(string output, Action<string> skipped)
	{
		Ensure.NotNull(output);
		Ensure.NotNull(skipped);

		StringBuilder decoded = new();

		foreach (string line in output.Split('\n'))
		{
			if (line.Length == 0)
			{
				continue;
			}

			byte[] bytes = Encoding.Latin1.GetBytes(line);

			try
			{
				decoded.Append(StrictUtf8.GetString(bytes)).Append('\n');
			}
			catch (DecoderFallbackException)
			{
				int separator = Array.IndexOf(bytes, (byte)' ');
				skipped(Convert.ToHexString(bytes, separator + 1, bytes.Length - separator - 1));
			}
		}

		return decoded.ToString();
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
