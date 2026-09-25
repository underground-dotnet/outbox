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
    /// occupying a worker. There is no way to switch it off. A Handler that ignores the token keeps its
    /// worker and its Group until it returns.
    /// </summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// What the outbox Lease adds to <see cref="HandlerTimeout"/>: the time left for the completion write,
    /// and how far each renewal extends a Lease whose Handler overran.
    /// </summary>
    /// <remarks>
    /// Not public, because a Lease shorter than the timeout guarantees double delivery. Settable only so
    /// tests need not wait out the production value.
    /// </remarks>
    internal TimeSpan LeaseMargin { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often the Lease of an overrunning Handler is renewed. A third of the margin, so one renewal can
    /// fail and the next still lands before the Lease expires.
    /// </summary>
    internal TimeSpan LeaseRenewalInterval => LeaseMargin / 3;

    /// <summary>
    /// How long an outbox worker's Lease runs for, measured from the claim. Derived from
    /// <see cref="HandlerTimeout"/> so the Handler's cancellation always fires with the margin to spare.
    /// A Handler that ignores it has its Lease renewed instead, so a message is never taken from a live
    /// worker. The inbox has nothing to expire.
    /// </summary>
    internal TimeSpan LeaseDuration => HandlerTimeout + LeaseMargin;

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
    /// Interval between cleanup cycles for processed messages.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

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

        EnsureWithin(nameof(HandlerTimeout), HandlerTimeout, ServiceConfigurationLimits.MinHandlerTimeout, ServiceConfigurationLimits.MaxHandlerTimeout);

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

        EnsureWithin(nameof(CompletedMessageRetention), CompletedMessageRetention, TimeSpan.Zero, ServiceConfigurationLimits.MaxCompletedMessageRetention);
        EnsureWithin(nameof(CleanupInterval), CleanupInterval, ServiceConfigurationLimits.MinCleanupInterval, ServiceConfigurationLimits.MaxCleanupInterval);
    }

    // upper bounds also keep values within what Task.Delay, CancelAfter and DateTime arithmetic accept
    private static void EnsureWithin(string propertyName, TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(propertyName, value, $"Must be between {min} and {max}.");
        }
    }
}
