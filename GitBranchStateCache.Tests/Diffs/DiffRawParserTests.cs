// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Diffs;

using ktsu.GitBranchStateCache.Diffs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class DiffRawParserTests
{
	private const string Blob = "9f8e7d6c5b4a39281706f5e4d3c2b1a098765432";
	private const string Other = "1122334455667788990011223344556677889900";
	private const string Zero = "0000000000000000000000000000000000000000";

	private static readonly string[] ExpectedOrder = ["a.uasset", "b.uasset", "c.uasset"];

	/// <summary>Builds a NUL-delimited record the way <c>git diff-tree -z</c> emits one.</summary>
	private static string Record(string metadata, params string[] paths) =>
		$"{metadata}\0{string.Join('\0', paths)}\0";

	[TestMethod]
	public void Parse_Modification_ReportsTheBlobAtTheSecondCommit()
	{
		string output = Record($":100644 100644 {Other} {Blob} M", "Content/Chars/Bar.uasset");

		DiffEntry entry = DiffRawParser.Parse(output).Single();

		Assert.AreEqual("Content/Chars/Bar.uasset", entry.Path);
		Assert.AreEqual(Blob, entry.Blob);
		Assert.AreEqual('M', entry.Status);
	}

	[TestMethod]
	public void Parse_Addition_ReportsTheNewBlob()
	{
		string output = Record($":000000 100644 {Zero} {Blob} A", "Content/Maps/New.umap");

		DiffEntry entry = DiffRawParser.Parse(output).Single();

		Assert.AreEqual(Blob, entry.Blob);
		Assert.AreEqual('A', entry.Status);
	}

	[TestMethod]
	public void Parse_Deletion_ReportsNoBlob()
	{
		// The all-zero object id is git saying the path does not exist on that side. Reporting it
		// verbatim would give a client a blob id to compare against that can never match anything.
		string output = Record($":100644 000000 {Blob} {Zero} D", "Content/Maps/Gone.umap");

		DiffEntry entry = DiffRawParser.Parse(output).Single();

		Assert.IsNull(entry.Blob);
		Assert.AreEqual('D', entry.Status);
	}

	[TestMethod]
	public void Parse_ModeChange_IsReported()
	{
		string output = Record($":100644 100755 {Blob} {Blob} T", "Scripts/build.sh");

		DiffEntry entry = DiffRawParser.Parse(output).Single();

		Assert.AreEqual('T', entry.Status);
		Assert.AreEqual(Blob, entry.Blob);
	}

	[TestMethod]
	public void Parse_Rename_ReportsBothPaths()
	{
		// Rename detection is off for the invocation this parses, but a configuration change that
		// turned it back on must not become a parse failure in production.
		string output = Record($":100644 100644 {Other} {Blob} R096", "Content/Old.uasset", "Content/New.uasset");

		IReadOnlyList<DiffEntry> entries = DiffRawParser.Parse(output);

		Assert.HasCount(2, entries);
		Assert.AreEqual("Content/Old.uasset", entries[0].Path);
		Assert.IsNull(entries[0].Blob);
		Assert.AreEqual('D', entries[0].Status);
		Assert.AreEqual("Content/New.uasset", entries[1].Path);
		Assert.AreEqual(Blob, entries[1].Blob);
	}

	[TestMethod]
	public void Parse_Copy_ReportsBothPaths()
	{
		string output = Record($":100644 100644 {Other} {Blob} C075", "Content/Source.uasset", "Content/Copy.uasset");

		Assert.HasCount(2, DiffRawParser.Parse(output));
	}

	[TestMethod]
	public void Parse_PathNeedingQuotingInTheDefaultFormat_IsReadVerbatim()
	{
		// Exactly why the -z form is used. In the default format this path comes back wrapped in
		// quotes with the space and the backslash escaped, and getting the unquoting wrong would mean
		// reporting a path that matches nothing on the client.
		const string awkward = "Content/Maps/A \"quoted\" name\\with backslash.umap";
		string output = Record($":100644 100644 {Other} {Blob} M", awkward);

		Assert.AreEqual(awkward, DiffRawParser.Parse(output).Single().Path);
	}

	[TestMethod]
	public void Parse_NonAsciiPath_IsReadVerbatim()
	{
		const string path = "Content/Personnages/Épée_日本語.uasset";
		string output = Record($":100644 100644 {Other} {Blob} M", path);

		Assert.AreEqual(path, DiffRawParser.Parse(output).Single().Path);
	}

	[TestMethod]
	public void Parse_ManyRecords_KeepsThemAllInOrder()
	{
		string output =
			Record($":100644 100644 {Other} {Blob} M", "a.uasset")
			+ Record($":000000 100644 {Zero} {Blob} A", "b.uasset")
			+ Record($":100644 000000 {Blob} {Zero} D", "c.uasset");

		IReadOnlyList<DiffEntry> entries = DiffRawParser.Parse(output);

		Assert.HasCount(3, entries);
		Assert.AreSequenceEqual(ExpectedOrder, entries.Select(entry => entry.Path));
	}

	[TestMethod]
	public void Parse_EmptyOutput_IsNoChanges() => Assert.IsEmpty(DiffRawParser.Parse(string.Empty));

	[TestMethod]
	public void Parse_RecordWithoutItsLeadingColon_Throws() =>
		Assert.ThrowsExactly<DiffFormatException>(
			() => DiffRawParser.Parse(Record($"100644 100644 {Other} {Blob} M", "a.uasset")));

	[TestMethod]
	public void Parse_RecordWithTooFewFields_Throws() =>
		Assert.ThrowsExactly<DiffFormatException>(
			() => DiffRawParser.Parse(Record($":100644 {Other} {Blob} M", "a.uasset")));

	[TestMethod]
	public void Parse_MalformedObjectId_Throws() =>
		Assert.ThrowsExactly<DiffFormatException>(
			() => DiffRawParser.Parse(Record($":100644 100644 {Other} deadbeef M", "a.uasset")));

	[TestMethod]
	public void Parse_MalformedMode_Throws() =>
		Assert.ThrowsExactly<DiffFormatException>(
			() => DiffRawParser.Parse(Record($":10064x 100644 {Other} {Blob} M", "a.uasset")));

	[TestMethod]
	public void Parse_RecordWithNoPath_Throws() =>
		Assert.ThrowsExactly<DiffFormatException>(
			() => DiffRawParser.Parse($":100644 100644 {Other} {Blob} M\0"));

	[TestMethod]
	public void Parse_RenameMissingItsSecondPath_Throws() =>
		Assert.ThrowsExactly<DiffFormatException>(
			() => DiffRawParser.Parse(Record($":100644 100644 {Other} {Blob} R100", "only-one.uasset")));

	[TestMethod]
	public void Parse_MalformedRecord_IsNeverSkipped()
	{
		// The property that matters more than any individual malformed shape: a record this parser
		// cannot read never turns into a shorter answer. Silently dropping one means silently failing
		// to warn someone that the asset they are about to lock is stale.
		string output =
			Record($":100644 100644 {Other} {Blob} M", "good.uasset")
			+ Record("garbage", "bad.uasset");

		Assert.ThrowsExactly<DiffFormatException>(() => DiffRawParser.Parse(output));
	}
}
