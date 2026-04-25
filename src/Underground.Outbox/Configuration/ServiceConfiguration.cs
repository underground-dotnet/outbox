using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public abstract class ServiceConfiguration
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

    internal List<HandlerRegistration> HandlerRegistrations { get; } = [];

    public HandlerRegistrationBuilder AddHandler<TMessageHandlerType>()
    {
        return AddHandler(typeof(TMessageHandlerType));
    }

    public HandlerRegistrationBuilder AddHandler<TMessageHandlerType>(ServiceLifetime serviceLifetime)
    {
        return AddHandler(typeof(TMessageHandlerType), serviceLifetime);
    }

#pragma warning disable CA1716 // Identifiers should not match keywords
    public abstract HandlerRegistrationBuilder AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient);
#pragma warning restore CA1716 // Identifiers should not match keywords

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

        foreach (var registration in HandlerRegistrations)
        {
            foreach (var policy in registration.ExceptionPolicies)
            {
                if (!typeof(Exception).IsAssignableFrom(policy.ExceptionType))
                {
                    throw new ArgumentException($"Configured type {policy.ExceptionType} must derive from Exception.");
                }
            }
        }
    }
}
