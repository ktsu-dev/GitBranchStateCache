// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Integration;

using System.IO.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// What counts as "the same repository" once a request has been accepted.
/// </summary>
/// <remarks>
/// The upstream registry and the allow-list both match case insensitively on purpose, so two callers
/// can address one repository by two spellings — of the repository path, of the upstream key, or of
/// both — and both be served. Everything derived afterwards — the mirror directory, the coalescing
/// key, the diff cache key, the admission key — compares ordinally, so unless the key is canonicalised
/// once at the point it is accepted, one repository quietly becomes two of everything. That is the
/// whole of this service's purpose inverted: it would do the work twice rather than once.
/// </remarks>
[TestClass]
public class RepositoryIdentityTests
{
	private const string ClientBase = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string MainTip = "cccccccccccccccccccccccccccccccccccccccc";
	private const string ForkPoint = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
	private const string Credential = "Basic dXNlcjp0b2tlbg==";

	/// <summary>The repository as the allow-list spells it.</summary>
	private const string ConfiguredSpelling = "/v1/github/studio/game.git/state";

	/// <summary>The same repository as a client that cloned it with different casing spells it.</summary>
	private const string CallerSpelling = "/v1/github/Studio/Game.git/state";

	/// <summary>
	/// The same repository again, reached through the upstream key spelled the way a forge brands
	/// itself rather than the way the configuration keys it.
	/// </summary>
	private const string UpstreamCallerSpelling = "/v1/GitHub/studio/game.git/state";

	private const string Body = $$"""{"base":"{{ClientBase}}","branchPatterns":["origin/main"]}""";

	private static void Seed(ScriptedGit git)
	{
		git.Branches["main"] = MainTip;
		git.Commits.Add(ClientBase);
		git.MergeBases[$"{ClientBase} {MainTip}"] = ForkPoint;
		git.Diffs[$"{ForkPoint} {MainTip}"] =
			":100644 100644 2222222222222222222222222222222222222222 1111111111111111111111111111111111111111 M\0Content/Chars/Bar.uasset\0";
	}

	private static HttpRequestMessage Request(string url)
	{
		HttpRequestMessage request = new(HttpMethod.Post, url)
		{
			Content = new StringContent(Body, Encoding.UTF8, "application/json"),
		};

		request.Headers.Authorization = AuthenticationHeaderValue.Parse(Credential);
		return request;
	}

	[TestMethod]
	public async Task TwoSpellingsOfOneRepository_AreBothServed()
	{
		// The premise the rest of this class rests on: the allow-list is case insensitive, so the
		// casing a client happened to clone with is not a reason to refuse it.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage configured = await fixture.Client.SendAsync(Request(ConfiguredSpelling));
		using HttpResponseMessage caller = await fixture.Client.SendAsync(Request(CallerSpelling));

		Assert.AreEqual(HttpStatusCode.OK, configured.StatusCode);
		Assert.AreEqual(HttpStatusCode.OK, caller.StatusCode);
	}

	[TestMethod]
	public async Task TwoSpellingsOfOneRepository_ShareOneMirrorFetchDiffAndAdmission()
	{
		// Two callers, one repository, one of everything. Without a canonical path each of these counts
		// is two: two bare clones of the same repository on the volume, two ls-remote probes of the
		// same credential, and two computations of the same diff.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage configured = await fixture.Client.SendAsync(Request(ConfiguredSpelling));
		using HttpResponseMessage caller = await fixture.Client.SendAsync(Request(CallerSpelling));

		Assert.AreEqual(HttpStatusCode.OK, configured.StatusCode);
		Assert.AreEqual(HttpStatusCode.OK, caller.StatusCode);

		Assert.AreEqual(1, fixture.Git.CountOf("clone"));
		Assert.AreEqual(1, fixture.Git.CountOf("ls-remote"));
		Assert.AreEqual(1, fixture.Git.CountOf("diff-tree"));
	}

	[TestMethod]
	public async Task TwoSpellingsOfOneRepository_ProduceOneMirrorDirectory()
	{
		// Asserted against the volume rather than against a count of clones, because this is the cost
		// that persists: the duplicate mirror stays on disk long after the request that made it.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage configured = await fixture.Client.SendAsync(Request(ConfiguredSpelling));
		using HttpResponseMessage caller = await fixture.Client.SendAsync(Request(CallerSpelling));

		IDirectory directory = fixture.FileSystem.Directory;

		Assert.HasCount(
			1,
			directory.GetDirectories(ServiceFixture.MirrorRoot, "mirror.git", SearchOption.AllDirectories));
	}

	[TestMethod]
	public async Task ARequestSpelledDifferentlyFromTheAllowList_MirrorsUnderTheCanonicalPath()
	{
		// The canonical form is lower case, so an operator reading the volume sees one directory per
		// repository whatever casing the clients that asked for it happened to use.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(Request(CallerSpelling));

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

		Assert.IsTrue(fixture.FileSystem.Directory.Exists(fixture.FileSystem.Path.Combine(
			ServiceFixture.MirrorRoot,
			"github",
			"studio",
			"game.git",
			"mirror.git")));
	}

	[TestMethod]
	public async Task TwoSpellingsOfOneUpstream_AreBothServed()
	{
		// The premise for the upstream key, matching the one above for the repository path: the registry
		// and the allow-list both resolve it case insensitively, so a caller that writes the forge's own
		// branding rather than the configuration key is still addressing the configured upstream.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage configured = await fixture.Client.SendAsync(Request(ConfiguredSpelling));
		using HttpResponseMessage caller = await fixture.Client.SendAsync(Request(UpstreamCallerSpelling));

		Assert.AreEqual(HttpStatusCode.OK, configured.StatusCode);
		Assert.AreEqual(HttpStatusCode.OK, caller.StatusCode);
	}

	[TestMethod]
	public async Task TwoSpellingsOfOneUpstream_ShareOneMirrorFetchDiffAndAdmission()
	{
		// The same duplication the repository-path case guards against, reached through the sibling half
		// of the key instead. One repository, so one clone, one credential probe, and one diff.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage configured = await fixture.Client.SendAsync(Request(ConfiguredSpelling));
		using HttpResponseMessage caller = await fixture.Client.SendAsync(Request(UpstreamCallerSpelling));

		Assert.AreEqual(HttpStatusCode.OK, configured.StatusCode);
		Assert.AreEqual(HttpStatusCode.OK, caller.StatusCode);

		Assert.AreEqual(1, fixture.Git.CountOf("clone"));
		Assert.AreEqual(1, fixture.Git.CountOf("ls-remote"));
		Assert.AreEqual(1, fixture.Git.CountOf("diff-tree"));
	}

	[TestMethod]
	public async Task TwoSpellingsOfOneUpstream_ProduceOneMirrorDirectory()
	{
		// Asserted against the volume, because an upstream key that is not canonicalised becomes a
		// second top-level directory holding a complete duplicate of every repository under it.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage configured = await fixture.Client.SendAsync(Request(ConfiguredSpelling));
		using HttpResponseMessage caller = await fixture.Client.SendAsync(Request(UpstreamCallerSpelling));

		IDirectory directory = fixture.FileSystem.Directory;

		Assert.HasCount(
			1,
			directory.GetDirectories(ServiceFixture.MirrorRoot, "mirror.git", SearchOption.AllDirectories));
	}

	[TestMethod]
	public async Task ARequestWithTheUpstreamSpelledDifferently_MirrorsUnderTheCanonicalUpstream()
	{
		// The canonical form is lower case for the upstream key as much as for the path, so the volume
		// carries one directory per configured upstream whatever casing reached the service.
		await using ServiceFixture fixture = await ServiceFixture.StartAsync();
		Seed(fixture.Git);

		using HttpResponseMessage response = await fixture.Client.SendAsync(Request(UpstreamCallerSpelling));

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

		Assert.IsTrue(fixture.FileSystem.Directory.Exists(fixture.FileSystem.Path.Combine(
			ServiceFixture.MirrorRoot,
			"github",
			"studio",
			"game.git",
			"mirror.git")));
	}
}
