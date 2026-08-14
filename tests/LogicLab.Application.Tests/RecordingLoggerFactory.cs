using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Tests;

internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<RecordedLog> entries = new();

    public IReadOnlyList<RecordedLog> Entries => [.. entries];

    public void AddProvider(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(categoryName);
        return new RecordingLogger(categoryName, entries);
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(
        string category,
        ConcurrentQueue<RecordedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values
                    .Where(pair => pair.Key != "{OriginalFormat}")
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
                : [];
            entries.Enqueue(new RecordedLog(
                category,
                logLevel,
                eventId,
                exception,
                properties));
        }
    }
}

internal sealed record RecordedLog(
    string Category,
    LogLevel Level,
    EventId EventId,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);
