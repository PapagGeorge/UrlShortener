using Microsoft.Extensions.Logging;

namespace UrlShortener.Domain.UnitTests.BuildingBlocks;

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>Captures log entries so tests can assert on what was logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
}
