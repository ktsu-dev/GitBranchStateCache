// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Tool;

using ktsu.GitBranchStateCache.Tool;

[TestClass]
public class MirrorRootFlagTests
{
	[TestMethod]
	public void MirrorRoot_RelativeDirectory_ResolvesAgainstTheWorkingDirectory()
	{
		// The options validator refuses a root that is not fully qualified, so passing
		// `--mirror-root ./mirrors` through verbatim aborted startup with a validation stack trace.
		string resolved = Program.ResolveMirrorRoot("./mirrors");

		Assert.IsTrue(Path.IsPathFullyQualified(resolved), resolved);
		Assert.AreEqual(Path.Combine(Environment.CurrentDirectory, "mirrors"), resolved);
	}

	[TestMethod]
	public async Task MirrorRoot_RelativeDirectory_IsAcceptedByTheCommandLine()
	{
		// The invalid upstream stops the run after the flags are read and before a server starts, so a
		// relative --mirror-root reaches the same one-line failure path as any other flag mistake.
		int exitCode = await Program.Main(["--mirror-root", "./mirrors", "--upstream", "not-a-name-url-pair"]);

		Assert.AreEqual(1, exitCode);
	}

	[TestMethod]
	public void MirrorRoot_FullyQualifiedDirectory_IsKeptAsGiven()
	{
		string root = Path.Combine(
			Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
			"gitbranchstatecache");

		Assert.AreEqual(root, Program.ResolveMirrorRoot(root));
	}
}
