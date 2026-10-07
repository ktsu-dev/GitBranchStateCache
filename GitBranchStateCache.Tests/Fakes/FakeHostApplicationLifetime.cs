// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Fakes;

using Microsoft.Extensions.Hosting;

/// <summary>
/// A host lifetime whose shutdown a test triggers by hand.
/// </summary>
internal sealed class FakeHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
	private readonly CancellationTokenSource _started = new();
	private readonly CancellationTokenSource _stopping = new();
	private readonly CancellationTokenSource _stopped = new();

	/// <inheritdoc />
	public CancellationToken ApplicationStarted => _started.Token;

	/// <inheritdoc />
	public CancellationToken ApplicationStopping => _stopping.Token;

	/// <inheritdoc />
	public CancellationToken ApplicationStopped => _stopped.Token;

	/// <inheritdoc />
	public void StopApplication() => _stopping.Cancel();

	/// <inheritdoc />
	public void Dispose()
	{
		_started.Dispose();
		_stopping.Dispose();
		_stopped.Dispose();
	}
}
