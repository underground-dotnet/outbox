using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

public abstract class ServiceConfiguration<TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Maximum number of Groups that can be processed concurrently.
    /// </summary>
    public int MaxConcurrentGroups { get; set; } = 4;

    /// <summary>
    /// Delay in milliseconds between processing cycles when messages are successfully processed.
    /// </summary>
    public int ProcessingDelayMilliseconds { get; set; } = 4000;

    /// <summary>
    /// Delay before a message that failed for the first time is offered again. Every further failure
    /// doubles it, up to <see cref="MaxBackoff"/>.
    /// </summary>
    public TimeSpan BackoffBase { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ceiling the doubling stops at, so that a message which recovers after a long outage is still
    /// picked up within a predictable time rather than after an ever-growing wait. It bounds the
    /// doubling rather than the delay itself: <see cref="BackoffJitter"/> is applied afterwards, so an
    /// actual delay may exceed this by the jitter proportion.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Proportion by which each retry delay is randomly varied, either way: 0.2 means plus or minus
    /// 20%. It keeps Groups that all failed against one shared dependency from retrying in lockstep.
    /// Set to 0 for exact delays.
    /// </summary>
    public double BackoffJitter { get; set; } = 0.2;

    /// <summary>
    /// Retention period for processed messages before they are eligible for cleanup.
    /// </summary>
    public TimeSpan ProcessedMessageRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    ///  Delay in seconds between cleanup cycles for processed messages.
    /// </summary>
    public int CleanupDelaySeconds { get; set; } = 3600;

    internal readonly List<HandlerRegistration<TEntity>> Registrations = [];

    internal readonly GlobalPolicyStore<TEntity> GlobalPolicies = new();
    public PolicyBuilder<TEntity> Policies { get; }

    protected ServiceConfiguration()
    {
        Policies = new PolicyBuilder<TEntity>(GlobalPolicies);
    }

    internal void Validate()
    {
        if (MaxConcurrentGroups <= 0)
        {
            throw new ArgumentOutOfRangeException($"MaxConcurrentGroups ({MaxConcurrentGroups}) must be greater than 0.");
        }

        if (ProcessingDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException($"ProcessingDelayMilliseconds ({ProcessingDelayMilliseconds}) cannot be negative.");
        }

        if (BackoffBase <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException($"BackoffBase ({BackoffBase}) must be greater than zero.");
        }

        if (MaxBackoff < BackoffBase)
        {
            throw new ArgumentOutOfRangeException($"MaxBackoff ({MaxBackoff}) cannot be shorter than BackoffBase ({BackoffBase}).");
        }

        // a jitter of 1 or more could produce a delay of zero or a negative one, which would retry immediately
        if (BackoffJitter is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException($"BackoffJitter ({BackoffJitter}) must be at least 0 and less than 1.");
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
