using Microsoft.Extensions.Logging;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>Records every log entry with its structured properties, for tests of log events (design 18).</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>The entries logged so far.</summary>
    public List<(LogLevel Level, EventId EventId, IReadOnlyDictionary<string, object?> Properties)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(p => p.Key, p => p.Value)
            : [];
        lock (Entries)
        {
            Entries.Add((logLevel, eventId, properties));
        }
    }
}
