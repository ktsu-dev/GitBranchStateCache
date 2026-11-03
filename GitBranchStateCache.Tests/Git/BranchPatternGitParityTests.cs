// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Git;

using System.Diagnostics;
using System.Text;
using ktsu.GitBranchStateCache.Refs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Runs patterns through both <see cref="BranchPattern"/> and <c>git branch -r --list</c> over the
/// same refs, because the README promises they pick out the same branches.
/// </summary>
[TestClass]
public class BranchPatternGitParityTests
{
	private static readonly string[] BranchNames =
	[
		"main", "Main", "develop", "release/1.0", "release/2.0", "release-1.0", "release-x", "v1.0", "v1x0",
		"v-1", "feature/v1.2", "a]c", "abc", "b", "Z", "7", "_",
		"é", "café", "cafe", "cafés", "naïve", "😀", "x😀", "日本",
	];

	private static readonly string[] Patterns =
	[
		"origin/main", "origin/*", "*/main", "origin/release/*", "origin/release/?.0", "origin/mai.",
		"origin/v1.0", "origin/release-[0-9]*", "origin/[abc]", "origin/a[]b]c", "origin/a[!]]c",
		"origin/v[-.]1", "origin/v[.-]1", "origin/release-[[:digit:]]*", "origin/[[:alnum:]]",
		"origin/[[:alpha:]_-]", "origin/[![:upper:]]", "origin/?", "origin/??", "origin/???",
		"origin/????", "origin/[é]", "origin/[!a]", "origin/caf?", "origin/caf??", "origin/caf???",
		"origin/café", "origin/caf\\é", "origin/caf*", "origin/*é", "origin/caf[!e]*", "origin/na?ve",
		"origin/na??ve", "origin/x?", "origin/x????", "origin/[😀]", "origin/日?", "origin/日???",
		"origin/[[:print:]]", "origin/[[:punct:]]", "origin/[!x]*",
	];

	private static string _repository = string.Empty;

	[ClassInitialize]
	public static void BuildFixture(TestContext context)
	{
		_repository = Path.Combine(Path.GetTempPath(), $"gbsc-parity-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_repository);

		Git("init", "--quiet", "--initial-branch=main");
		Git("-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "--quiet", "--allow-empty", "-m", "initial");
		string head = Git("rev-parse", "HEAD").Trim();

		// Written straight into packed-refs rather than as loose ref files, so a case-insensitive file
		// system (macOS, Windows) cannot fold "main" and "Main" into one ref. Unsorted, so the header
		// does not claim otherwise.
		StringBuilder packed = new("# pack-refs with: peeled fully-peeled \n");
		foreach (string name in BranchNames)
		{
			packed.Append(head).Append(" refs/remotes/origin/").Append(name).Append('\n');
		}

		File.WriteAllText(Path.Combine(_repository, ".git", "packed-refs"), packed.ToString(), new UTF8Encoding(false));
	}

	[ClassCleanup]
	public static void RemoveFixture()
	{
		try
		{
			Directory.Delete(_repository, recursive: true);
		}
		catch (IOException)
		{
			// A leftover fixture in the temp directory is not worth failing a run over.
		}
	}

	[TestMethod]
	public void EveryPattern_PicksOutTheSameBranchesAsGitBranchList()
	{
		string[] refs = [.. BranchNames.Select(name => $"origin/{name}")];
		List<string> disagreements = [];

		foreach (string text in Patterns)
		{
			Assert.IsTrue(BranchPattern.TryParse(text, out BranchPattern? pattern, out string? failure), failure);

			SortedSet<string> ours = new(refs.Where(pattern!.Matches), StringComparer.Ordinal);
			SortedSet<string> gits = new(
				Git("branch", "-r", "--list", "--format=%(refname:lstrip=2)", text)
					.Split('\n', StringSplitOptions.RemoveEmptyEntries),
				StringComparer.Ordinal);

			if (!ours.SetEquals(gits))
			{
				disagreements.Add($"{text}: git [{string.Join(", ", gits)}], BranchPattern [{string.Join(", ", ours)}]");
			}
		}

		Assert.IsEmpty(disagreements, string.Join(Environment.NewLine, disagreements));
	}

	private static string Git(params string[] arguments)
	{
		ProcessStartInfo startInfo = new()
		{
			FileName = "git",
			WorkingDirectory = _repository,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = new UTF8Encoding(false),
			StandardErrorEncoding = new UTF8Encoding(false),
		};

		// Nothing configured for whoever runs the tests may change what git lists.
		startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
		startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";

		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("git could not be started.");

		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();

		return process.ExitCode == 0
			? output
			: throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
	}
}
