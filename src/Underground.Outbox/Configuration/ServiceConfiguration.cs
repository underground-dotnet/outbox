using System.Diagnostics.CodeAnalysis;

using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

public abstract class ServiceConfiguration<TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Maximum number of Groups handled concurrently, and the number of workers that run. A value of one
    /// means strictly serial handling across all Groups, not one message per Group.
    /// </summary>
    public int MaxConcurrentGroups { get; set; } = 2;

    /// <summary>
    /// How often the pool is woken to look for work, in milliseconds. A cadence rather than a per-worker
    /// idle timer: every waiting worker is released at once. A commit in this process wakes workers
    /// immediately, so this bounds the latency of work nothing told us about - and is what makes delivery
    /// guaranteed rather than dependent on a notification arriving.
    /// </summary>
    public int ProcessingDelayMilliseconds { get; set; } = 10_000;

    /// <summary>
    /// The time a Handler is given to complete. When it elapses the Handler's token is cancelled and the
    /// message is recorded as a failed attempt, so a hung call costs its own Group a backoff rather than
    /// occupying a worker. There is no way to switch it off.
    /// </summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// What the outbox Lease adds to <see cref="HandlerTimeout"/>: the time left for the completion write.
    /// A constant, because a configurable Lease shorter than the timeout guarantees double delivery.
    /// </summary>
    private const int LeaseMarginSeconds = 15;

    /// <summary>
    /// How long an outbox worker's Lease runs for, measured from the claim. Derived from
    /// <see cref="HandlerTimeout"/> so the Handler's cancellation always fires with the margin to spare
    /// and a message can never be taken from a live worker. The inbox has nothing to expire.
    /// </summary>
    internal TimeSpan LeaseDuration => HandlerTimeout + TimeSpan.FromSeconds(LeaseMarginSeconds);

    /// <summary>
    /// Delay before a message that failed for the first time is offered again. Every further failure
    /// doubles it, up to <see cref="MaxBackoff"/>.
    /// </summary>
    public TimeSpan BackoffBase { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ceiling the doubling stops at. It bounds the doubling rather than the delay itself:
    /// <see cref="BackoffJitter"/> is applied afterwards, so an actual delay may exceed this.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Proportion by which each retry delay is randomly varied, either way: 0.2 means plus or minus 20%.
    /// Keeps Groups that failed against one shared dependency from retrying in lockstep. 0 for exact delays.
    /// </summary>
    public double BackoffJitter { get; set; } = 0.2;

    /// <summary>
    /// Retention period for processed messages before they are eligible for cleanup.
    /// </summary>
    public TimeSpan CompletedMessageRetention { get; set; } = TimeSpan.FromDays(7);

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

    // the "argument" here is a configuration property of this instance rather than a parameter of
    // Validate, so both analyzers see a paramName they cannot match against a parameter list
    [SuppressMessage("Meziantou.Analyzer", "MA0015:Specify the parameter name in ArgumentException", Justification = "paramName names the offending configuration property")]
    [SuppressMessage("Major Code Smell", "S3928:Parameter names used into ArgumentException constructors should match an existing one ", Justification = "paramName names the offending configuration property")]
    internal void Validate()
    {
        if (MaxConcurrentGroups <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentGroups), MaxConcurrentGroups, "Must be greater than 0.");
        }

        if (ProcessingDelayMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ProcessingDelayMilliseconds), ProcessingDelayMilliseconds, "Must be greater than 0.");
        }

        if (HandlerTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(HandlerTimeout), HandlerTimeout, "Must be greater than zero.");
        }

        if (BackoffBase <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BackoffBase), BackoffBase, "Must be greater than zero.");
        }

        if (MaxBackoff < BackoffBase)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBackoff), MaxBackoff, $"Cannot be shorter than BackoffBase ({BackoffBase}).");
        }

        // a jitter of 1 or more could produce a zero or negative delay, which would retry immediately
        if (BackoffJitter is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BackoffJitter), BackoffJitter, "Must be at least 0 and less than 1.");
        }

        if (CompletedMessageRetention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CompletedMessageRetention), CompletedMessageRetention, "Cannot be negative.");
        }

        if (CleanupDelaySeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CleanupDelaySeconds), CleanupDelaySeconds, "Must be greater than 0.");
        }
    }
}
