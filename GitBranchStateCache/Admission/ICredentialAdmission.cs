// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Admission;

/// <summary>
/// Remembers, briefly, that an upstream accepted a credential for a repository.
/// </summary>
/// <remarks>
/// This service holds read-only copies of source. Serving branch state from a mirror to whoever asks
/// for it would be an authorization bypass for anyone who can route to it, so nothing is ever served
/// to a caller whose own credential has not been proven against the forge. This is what lets that
/// check happen once per credential per interval instead of once per request, without ever inventing
/// an answer: an entry is only ever created by an upstream call that actually succeeded.
/// <para>
/// It must be impossible to bypass, including when the forge is unreachable. There is no path here
/// that admits on a failure to ask, because failing open on an outage would mean the whole of a
/// studio's source becoming readable by anyone who could reach the service for as long as the outage
/// lasted.
/// </para>
/// <para>
/// This is not a credential store. Nothing here can be used to authenticate to an upstream, and no
/// credential is retained.
/// </para>
/// </remarks>
public interface ICredentialAdmission
{
	/// <summary>
	/// Reports whether this credential was recently accepted upstream for this repository.
	/// </summary>
	/// <param name="upstream">The upstream key.</param>
	/// <param name="repositoryPath">The repository the credential was accepted for.</param>
	/// <param name="authorization">The client's Authorization header, exactly as sent.</param>
	/// <returns><see langword="true"/> when an unexpired admission exists.</returns>
	public bool IsAdmitted(string upstream, string repositoryPath, string? authorization);

	/// <summary>
	/// Records that an upstream accepted this credential for this repository.
	/// </summary>
	/// <remarks>
	/// Only ever called after a real upstream success. Calling it anywhere else would turn this
	/// service into the authority it is designed never to be.
	/// </remarks>
	/// <param name="upstream">The upstream key.</param>
	/// <param name="repositoryPath">The repository the credential was accepted for.</param>
	/// <param name="authorization">The client's Authorization header, exactly as sent.</param>
	public void Admit(string upstream, string repositoryPath, string? authorization);
}
