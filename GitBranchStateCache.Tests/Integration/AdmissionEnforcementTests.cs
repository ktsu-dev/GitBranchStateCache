// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Integration;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The checks that decide whether anything is served at all, and in what order.
/// </summary>
/// <remarks>
/// This service holds read-only copies of a studio's source, which makes it a more attractive target
/// than a blob cache. Admission is the only control over who is served, and the allow-list is the
/// only control over what is ever cloned; both properties are asserted here as negatives, because
/// what matters is what does <em>not</em> happen.
/// </remarks>
[TestClass]
public class AdmissionEnforcementTests
{
	private const string ClientBase = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string MainTip = "cccccccccccccccccccccccccccccccccccccccc";
	private const string Credential = "Basic dXNlcjp0b2tlbg==";

	private const string Body = $$"""{"base":"{{ClientBase}}","branchPatterns":["origin/main"]}""";

	private static void Seed(ScriptedGit git)
	{
		git.Branches["main"] = MainTip;
		git.Commits.Add(ClientBase);
		git.MergeBases[$"{ClientBase} {MainTip}"] = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
	}

	private static HttpRequestMessage Request(string url, string? credential = Credential)
	{
		HttpRequestMessage request = new(HttpMethod.Post, url)
		{
			Content = new StringContent(Body, Encoding.UTF8, "application/json"),
		};

		if (credential is not null)
		{
			request.Headers.Authorization = AuthenticationHeaderValue.Parse(credential);
		}

		return request;
	}

	[TestMethod]
	public async Task UnlistedRepository_Is404_AndProducesNoUpstreamCallAtAll()
	{
		// The ordering that matters most in this service. Reversed, an unlisted repository would still
		// be probed against the forge with the caller's credential before being refused, which turns
		// this into an oracle for which repositories a credential can read.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			Request("/v1/github/studio/secret.git/state"));

		Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
		Assert.AreEqual(0, fixture.Git.CountOf("ls-remote"));
	}

	[TestMethod]
	public async Task UnlistedRepository_CreatesNoMirrorDirectory()
	{
		// One request for an unlisted repository would otherwise be a permanent clone of it onto a
		// shared volume, sized by the repository rather than by the request, that nothing ever evicts.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			Request("/v1/github/studio/secret.git/state"));

		Assert.AreEqual(0, fixture.Git.CountOf("clone"));
		Assert.IsFalse(fixture.FileSystem.Directory.Exists(
			fixture.FileSystem.Path.Combine(ServiceFixture.MirrorRoot, "github", "studio", "secret.git")));
	}

	[TestMethod]
	public async Task UnknownUpstream_Is404_AndProducesNoUpstreamCall()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			Request("/v1/nosuchforge/studio/game.git/state"));

		Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
		Assert.AreEqual(0, fixture.Git.CountOf("ls-remote"));
	}

	[TestMethod]
	public async Task RefusedCredential_IsNeverServed_EvenWhenTheMirrorAlreadyHoldsTheData()
	{
		// The case a cache gets wrong: the answer is sitting right there, already fetched for someone
		// else, and serving it would be an authorization bypass for anyone who can route to this
		// service.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage warm = await fixture.Client.SendAsync(Request("/v1/github/studio/game.git/state"));
		Assert.AreEqual(HttpStatusCode.OK, warm.StatusCode);

		fixture.Git.AdmitsCredentials = false;

		using HttpResponseMessage refused = await fixture.Client.SendAsync(
			Request("/v1/github/studio/game.git/state", credential: "Basic c29tZW9uZTplbHNl"));

		Assert.AreEqual(HttpStatusCode.Unauthorized, refused.StatusCode);
	}

	[TestMethod]
	public async Task NoCredential_Is401_AndProducesNoUpstreamCall()
	{
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			Request("/v1/github/studio/game.git/state", credential: null));

		Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.AreEqual(0, fixture.Git.CountOf("ls-remote"));
	}

	[TestMethod]
	public async Task AdmissionExpires_AndIsProvenAgain()
	{
		// The window in which a credential revoked upstream still reads branch state has to actually
		// close.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage first = await fixture.Client.SendAsync(Request("/v1/github/studio/game.git/state"));
		fixture.Time.Advance(TimeSpan.FromMinutes(2));
		using HttpResponseMessage second = await fixture.Client.SendAsync(Request("/v1/github/studio/game.git/state"));

		Assert.AreEqual(2, fixture.Git.CountOf("ls-remote"));
	}

	[TestMethod]
	public async Task ARepositoryPathThatCannotBeLaidOutOnDisk_Is404()
	{
		// The second line of defence, behind the allow-list. A traversal that reaches the filesystem
		// is not the place to discover that the first line had a gap.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync(new Dictionary<string, string?>
		{
			["GitBranchStateCache:Upstreams:github:Repositories:0"] = "studio/**",
		});

		using HttpResponseMessage response = await fixture.Client.SendAsync(
			Request("/v1/github/studio/..%2F..%2Fescape/state"));

		Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
		Assert.AreEqual(0, fixture.Git.CountOf("clone"));
	}
}
