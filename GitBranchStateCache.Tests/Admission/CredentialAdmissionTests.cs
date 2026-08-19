// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Admission;

using ktsu.GitBranchStateCache.Admission;
using ktsu.GitBranchStateCache.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class CredentialAdmissionTests
{
	private const string Repository = "studio/game.git";
	private const string Credential = "Basic dXNlcjp0b2tlbg==";

	private static (CredentialAdmission Admission, FakeTimeProvider Time) Build(TimeSpan? ttl = null)
	{
		GitBranchStateCacheOptions options = new() { AdmissionTtl = ttl ?? TimeSpan.FromMinutes(1) };
		FakeTimeProvider time = new(new DateTimeOffset(2026, 8, 19, 9, 47, 0, TimeSpan.Zero));

		return (new CredentialAdmission(Options.Create(options), time), time);
	}

	[TestMethod]
	public void IsAdmitted_BeforeAnyUpstreamSuccess_IsFalse()
	{
		// The whole point: nothing is admitted until an upstream actually said yes.
		(CredentialAdmission admission, _) = Build();

		Assert.IsFalse(admission.IsAdmitted("github", Repository, Credential));
	}

	[TestMethod]
	public void IsAdmitted_AfterAdmit_IsTrue()
	{
		(CredentialAdmission admission, _) = Build();

		admission.Admit("github", Repository, Credential);

		Assert.IsTrue(admission.IsAdmitted("github", Repository, Credential));
	}

	[TestMethod]
	public void IsAdmitted_AfterTheTtl_IsFalseAgain()
	{
		// This is the window in which a credential revoked upstream still reads branch state. It has
		// to actually close.
		(CredentialAdmission admission, FakeTimeProvider time) = Build(TimeSpan.FromMinutes(1));

		admission.Admit("github", Repository, Credential);
		time.Advance(TimeSpan.FromMinutes(1));

		Assert.IsFalse(admission.IsAdmitted("github", Repository, Credential));
	}

	[TestMethod]
	public void IsAdmitted_ADifferentCredential_IsFalse()
	{
		(CredentialAdmission admission, _) = Build();

		admission.Admit("github", Repository, Credential);

		Assert.IsFalse(admission.IsAdmitted("github", Repository, "Basic c29tZW9uZTplbHNl"));
	}

	[TestMethod]
	public void IsAdmitted_ADifferentRepository_IsFalse()
	{
		// An admission proves read access to one repository and says nothing about any other.
		(CredentialAdmission admission, _) = Build();

		admission.Admit("github", Repository, Credential);

		Assert.IsFalse(admission.IsAdmitted("github", "studio/secret.git", Credential));
	}

	[TestMethod]
	public void IsAdmitted_ADifferentUpstream_IsFalse()
	{
		(CredentialAdmission admission, _) = Build();

		admission.Admit("github", Repository, Credential);

		Assert.IsFalse(admission.IsAdmitted("ado", Repository, Credential));
	}

	[TestMethod]
	public void IsAdmitted_NoCredential_IsFalse()
	{
		(CredentialAdmission admission, _) = Build();

		admission.Admit("github", Repository, null);

		Assert.IsFalse(admission.IsAdmitted("github", Repository, null));
	}

	[TestMethod]
	public void Key_CannotBeReSplitAcrossParts()
	{
		// Without a separator that cannot appear in either part, an upstream and a repository could be
		// re-split to match a different pair, which would admit a caller for something they never
		// proved.
		(CredentialAdmission admission, _) = Build();

		admission.Admit("github", "studio/game.git", Credential);

		Assert.IsFalse(admission.IsAdmitted("github/studio", "game.git", Credential));
	}
}
