// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Diffs;

/// <summary>
/// Thrown when git's raw diff output cannot be read.
/// </summary>
/// <remarks>
/// Thrown rather than the offending record being skipped, and it fails the whole request rather than
/// one branch. Silently dropping a record means silently failing to warn someone that the asset they
/// are about to lock is stale, which is the exact outcome this service exists to prevent. A request
/// that fails is one the client retries or falls back from; a request that quietly omits a path is one
/// nobody ever finds out about.
/// </remarks>
public sealed class DiffFormatException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="DiffFormatException"/> class.</summary>
	public DiffFormatException()
		: base("git produced raw diff output that could not be read.")
	{
	}

	/// <summary>Initializes a new instance of the <see cref="DiffFormatException"/> class.</summary>
	/// <param name="message">What was wrong with the output.</param>
	public DiffFormatException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="DiffFormatException"/> class.</summary>
	/// <param name="message">What was wrong with the output.</param>
	/// <param name="innerException">The underlying failure.</param>
	public DiffFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
