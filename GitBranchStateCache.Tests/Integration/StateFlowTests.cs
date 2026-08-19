// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Integration;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class StateFlowTests
{
	private const string ClientBase = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string OtherClientBase = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
	private const string MainTip = "cccccccccccccccccccccccccccccccccccccccc";
	private const string ReleaseTip = "dddddddddddddddddddddddddddddddddddddddd";
	private const string ForkPoint = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
	private const string ChangedBlob = "1111111111111111111111111111111111111111";
	private const string Credential = "Basic dXNlcjp0b2tlbg==";

	private const string StateUrl = "/v1/github/studio/game.git/state";

	private static readonly string[] MainOnly = ["origin/main"];

	/// <summary>Seeds the scripted git with a repository two branches have diverged in.</summary>
	private static void Seed(ScriptedGit git)
	{
		git.Branches["main"] = MainTip;
		git.Branches["release/1.0"] = ReleaseTip;
		git.Commits.Add(ClientBase);
		git.Commits.Add(OtherClientBase);
		git.MergeBases[$"{ClientBase} {MainTip}"] = ForkPoint;
		git.MergeBases[$"{OtherClientBase} {MainTip}"] = ForkPoint;
		git.MergeBases[$"{ClientBase} {ReleaseTip}"] = ForkPoint;
		git.Diffs[$"{ForkPoint} {MainTip}"] =
			$":100644 100644 2222222222222222222222222222222222222222 {ChangedBlob} M\0Content/Chars/Bar.uasset\0"
			+ ":100644 000000 3333333333333333333333333333333333333333 0000000000000000000000000000000000000000 D\0Content/Maps/Gone.umap\0";
	}

	private static HttpRequestMessage StateRequest(
		string body,
		string? credential = Credential,
		string url = StateUrl)
	{
		HttpRequestMessage request = new(HttpMethod.Post, url)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};

		if (credential is not null)
		{
			request.Headers.Authorization = AuthenticationHeaderValue.Parse(credential);
		}

		return request;
	}

	private static string Body(string @base = ClientBase, string patterns = "\"origin/main\"", string? paths = null) =>
		paths is null
			? $$"""{"base":"{{@base}}","branchPatterns":[{{patterns}}]}"""
			: $$"""{"base":"{{@base}}","branchPatterns":[{{patterns}}],"paths":[{{paths}}]}""";

	private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
		JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

	[TestMethod]
	public async Task Post_State_ReportsBlobIdsPerPathPerBranch()
	{
		// Blob ids rather than a verdict. The client compares them against its own working tree, which
		// is local, exact, and catches the changed-then-reverted case that the plugin's log-and-diff
		// intersection only approximates.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(StateRequest(Body()));

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
		JsonElement body = await ReadAsync(response);

		JsonElement changes = body.GetProperty("paths").GetProperty("Content/Chars/Bar.uasset");
		Assert.AreEqual("origin/main", changes[0].GetProperty("branch").GetString());
		Assert.AreEqual(ChangedBlob, changes[0].GetProperty("blob").GetString());
		Assert.AreEqual("M", changes[0].GetProperty("status").GetString());

		// A delete carries git's status letter and no blob, so a client can tell it from a change.
		JsonElement deleted = body.GetProperty("paths").GetProperty("Content/Maps/Gone.umap");
		Assert.AreEqual("D", deleted[0].GetProperty("status").GetString());
		Assert.AreEqual(JsonValueKind.Null, deleted[0].GetProperty("blob").ValueKind);

		Assert.IsFalse(body.GetProperty("partial").GetBoolean());
		Assert.AreEqual(MainTip, body.GetProperty("branches")[0].GetProperty("tip").GetString());
		Assert.AreEqual(ForkPoint, body.GetProperty("branches")[0].GetProperty("mergeBase").GetString());
	}

	[TestMethod]
	public async Task Post_State_OnlyNamesTheRequestedPaths()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			StateRequest(Body(paths: "\"Content/Chars/Bar.uasset\"")));

		JsonElement paths = (await ReadAsync(response)).GetProperty("paths");

		Assert.AreEqual(1, paths.EnumerateObject().Count());
		Assert.IsTrue(paths.TryGetProperty("Content/Chars/Bar.uasset", out _));
	}

	[TestMethod]
	public async Task Post_State_WildcardPattern_AnswersForEveryMatchingBranch()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			StateRequest(Body(patterns: "\"origin/*\"")));

		Assert.HasCount(2, (await ReadAsync(response)).GetProperty("branches").EnumerateArray().ToList());
	}

	[TestMethod]
	public async Task Post_State_TwoClientsOnDifferentBasesSharingAMergeBase_ComputeOneDiff()
	{
		// The reason the diff cache is keyed on the merge base. Every artist sits on a different
		// commit, and keying on the base they send would barely deduplicate anything.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage first = await fixture.Client.SendAsync(StateRequest(Body(ClientBase)));
		using HttpResponseMessage second = await fixture.Client.SendAsync(StateRequest(Body(OtherClientBase)));

		Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
		Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
		Assert.AreEqual(1, fixture.Git.CountOf("diff-tree"));
	}

	[TestMethod]
	public async Task Post_State_UnknownBase_Is409AndCarriesTheCurrentTips()
	{
		// A client that has not pushed in a long time, or that rewrote history. It is told the current
		// tips so it can decide what to do without a second request, and is expected to fall back to
		// its own local computation for that cycle.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			StateRequest(Body("9999999999999999999999999999999999999999")));

		Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
		JsonElement body = await ReadAsync(response);

		Assert.AreEqual("unknown-base", body.GetProperty("error").GetString());
		Assert.AreEqual(MainTip, body.GetProperty("branches")[0].GetProperty("tip").GetString());
	}

	[TestMethod]
	public async Task Post_State_WhenABranchHasNoMergeBase_IsPartialAndLabelled()
	{
		// Never reported as "nothing changed on that branch", which is the one wrong answer with real
		// consequences.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);
		fixture.Git.MergeBases.Remove($"{ClientBase} {MainTip}");

		using HttpResponseMessage response = await fixture.Client.SendAsync(StateRequest(Body()));
		JsonElement body = await ReadAsync(response);

		Assert.IsTrue(body.GetProperty("partial").GetBoolean());
		Assert.AreEqual("no-merge-base", body.GetProperty("branches")[0].GetProperty("error").GetString());
	}

	[TestMethod]
	public async Task Post_State_WhenADiffTimesOut_TheOtherBranchesStillAnswer()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);
		fixture.Git.DiffTimesOut = true;

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			StateRequest(Body(patterns: "\"origin/*\"")));
		JsonElement body = await ReadAsync(response);

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
		Assert.IsTrue(body.GetProperty("partial").GetBoolean());
		Assert.IsTrue(body.GetProperty("branches").EnumerateArray()
			.All(branch => branch.GetProperty("error").ValueKind != JsonValueKind.Null));
	}

	[TestMethod]
	public async Task Post_State_MalformedDiffOutput_FailsTheRequestRatherThanOmittingAPath()
	{
		// Silently dropping a record means silently failing to warn someone that the asset they are
		// about to lock is stale, so this is the one per-branch problem that fails the whole request.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);
		fixture.Git.Diffs[$"{ForkPoint} {MainTip}"] = "this is not a raw diff record\0path\0";

		using HttpResponseMessage response = await fixture.Client.SendAsync(StateRequest(Body()));

		Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
		Assert.AreEqual("diff-unreadable", (await ReadAsync(response)).GetProperty("error").GetString());
	}

	[TestMethod]
	public async Task Post_State_AFetchLandingMidRequest_DoesNotChangeThatRequestsAnswer()
	{
		// The explicit-object-id invariant, made into a test. The tips are read once and every later
		// step names those ids, so a branch moving underneath a request cannot tear its answer.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		const string movedTip = "7777777777777777777777777777777777777777";

		fixture.Git.Before = invocation =>
		{
			if (invocation.Arguments[0] == "merge-base")
			{
				fixture.Git.Branches["main"] = movedTip;
			}

			return Task.CompletedTask;
		};

		using HttpResponseMessage response = await fixture.Client.SendAsync(StateRequest(Body()));
		JsonElement body = await ReadAsync(response);

		Assert.AreEqual(MainTip, body.GetProperty("branches")[0].GetProperty("tip").GetString());
		Assert.IsTrue(body.GetProperty("paths").TryGetProperty("Content/Chars/Bar.uasset", out _));
	}

	[TestMethod]
	public async Task Post_State_WithNoBranchPatterns_Is400()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			StateRequest($$"""{"base":"{{ClientBase}}","branchPatterns":[]}"""));

		Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[TestMethod]
	[DataRow("HEAD")]
	[DataRow("main@{1}")]
	[DataRow("--upload-pack=evil")]
	[DataRow("aaaa")]
	public async Task Post_State_ABaseThatIsNotAnObjectId_Is400(string @base)
	{
		// git accepts a great deal more than object ids where one is expected, including things that
		// read as options. None of it reaches a command line.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			StateRequest(JsonSerializer.Serialize(new { @base, branchPatterns = MainOnly })));

		Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.AreEqual(0, fixture.Git.CountOf("merge-base"));
	}

	[TestMethod]
	public async Task Get_Branches_ResolvesPatternsToNamesAndTips()
	{
		// The other reason the plugin currently fetches on its heartbeat: its wildcard branch lookup
		// needs an up-to-date ref list and nothing else.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpRequestMessage request = new(
			HttpMethod.Get,
			"/v1/github/studio/game.git/branches?pattern=origin/release/*");
		request.Headers.Authorization = AuthenticationHeaderValue.Parse(Credential);

		using HttpResponseMessage response = await fixture.Client.SendAsync(request);
		JsonElement body = await ReadAsync(response);

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
		Assert.AreEqual("origin/release/1.0", body.GetProperty("branches")[0].GetProperty("name").GetString());
		Assert.AreEqual(ReleaseTip, body.GetProperty("branches")[0].GetProperty("tip").GetString());
	}

	[TestMethod]
	public async Task Get_State_WithTheWrongMethod_Is405()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(StateUrl);

		Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
	}

	[TestMethod]
	public async Task Get_Healthz_IsOk()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync("/healthz");

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
	}

	[TestMethod]
	public async Task Get_Readyz_IsReadyOnceTheStartupChecksHavePassed()
	{
		// The startup check proves the volume is writable and that git can actually be started, which
		// are the two ways this service can look healthy from outside and fail every request.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync("/readyz");

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
		Assert.AreEqual("ready", await response.Content.ReadAsStringAsync());
	}

	[TestMethod]
	public async Task Post_State_SecondRequestWithinTheRefsTtl_DoesNotFetchAgain()
	{
		// Every open editor in the studio asks about the same repositories on the same thirty second
		// heartbeat. Without this the service would multiply the fetch traffic rather than remove it.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage first = await fixture.Client.SendAsync(StateRequest(Body()));
		using HttpResponseMessage second = await fixture.Client.SendAsync(StateRequest(Body()));

		Assert.AreEqual(1, fixture.Git.CountOf("clone"));
		Assert.AreEqual(0, fixture.Git.CountOf("fetch"));
	}

	[TestMethod]
	public async Task Post_State_AfterTheRefsTtl_FetchesAgain()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage first = await fixture.Client.SendAsync(StateRequest(Body()));
		fixture.Time.Advance(TimeSpan.FromMinutes(1));
		using HttpResponseMessage second = await fixture.Client.SendAsync(StateRequest(Body()));

		Assert.AreEqual(1, fixture.Git.CountOf("fetch"));
	}

	[TestMethod]
	public async Task Post_State_ReportsHowOldTheRefsAre()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(StateRequest(Body()));
		JsonElement body = await ReadAsync(response);

		Assert.AreEqual(fixture.Time.GetUtcNow(), body.GetProperty("refsAsOf").GetDateTimeOffset());
	}
}
