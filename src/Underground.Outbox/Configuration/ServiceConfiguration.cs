using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

public abstract class ServiceConfiguration<TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Number of messages to process in a single batch.
    /// The whole batch is processed within a single transaction. If you want to have a transaction per message, set this to 1.
    /// </summary>
    public int BatchSize { get; set; } = 5;

    public int ParallelProcessingOfPartitions { get; set; } = 4;

    /// <summary>
    /// Delay in milliseconds between processing cycles when messages are successfully processed.
    /// </summary>
    public int ProcessingDelayMilliseconds { get; set; } = 4000;

    /// <summary>
    /// Retention period for processed messages before they are eligible for cleanup.
    /// </summary>
    public TimeSpan ProcessedMessageRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    ///  Delay in seconds between cleanup cycles for processed messages.
    /// </summary>
    public int CleanupDelaySeconds { get; set; } = 3600;

    internal readonly List<HandlerRegistration<TEntity>> Registrations = [];

    internal void Validate()
    {
        if (BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException($"BatchSize ({BatchSize}) must be greater than 0.");
        }

        if (ParallelProcessingOfPartitions <= 0)
        {
            throw new ArgumentOutOfRangeException($"ParallelProcessingOfPartitions ({ParallelProcessingOfPartitions}) must be greater than 0.");
        }

        if (ProcessingDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException($"ProcessingDelayMilliseconds ({ProcessingDelayMilliseconds}) cannot be negative.");
        }

        if (ProcessedMessageRetention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException($"ProcessedMessageRetention ({ProcessedMessageRetention}) cannot be negative.");
        }

        if (CleanupDelaySeconds <= 0)
        {
            throw new ArgumentOutOfRangeException($"CleanupDelaySeconds ({CleanupDelaySeconds}) must be greater than 0.");
        }
    }
}
