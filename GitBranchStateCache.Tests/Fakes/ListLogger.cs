// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Fakes;

using Microsoft.Extensions.Logging;

/// <summary>
/// A logger that records every formatted message it is given.
/// </summary>
/// <typeparam name="T">The category.</typeparam>
/// <param name="messages">Where the messages go.</param>
internal sealed class ListLogger<T>(List<string> messages) : ILogger<T>
{
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		lock (messages)
		{
			messages.Add(formatter(state, exception));
		}
	}
}
