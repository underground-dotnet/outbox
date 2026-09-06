using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Underground.OutboxTest;

/// <summary>
/// Keeps every log entry the library wrote, so that a test can assert on one the library reports rather
/// than throws. Used for the lost Lease, which is deliberately a warning and has no other observable
/// trace: the row simply stays as the newer worker left it.
/// </summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    /// <summary>Every entry written so far, oldest first.</summary>
    public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries;

    /// <summary>Hands out a logger that records into the one shared queue, whatever its category.</summary>
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_entries);

    /// <summary>Does nothing: the entries outlive the provider so a test can still read them.</summary>
    public void Dispose()
    {
        // nothing to release: the entries outlive the provider so a test can still read them
    }

    private sealed class RecordingLogger(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
