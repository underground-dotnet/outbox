using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed partial class ConcurrentProcessor<TEntity>(
    ILogger<ConcurrentProcessor<TEntity>> logger,
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration<TEntity> config
) where TEntity : class, IMessage
{
    private readonly ILogger<ConcurrentProcessor<TEntity>> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ServiceConfiguration<TEntity> _config = config;

    private readonly WorkSignal _workSignal = new();

    /// <summary>
    /// Runs one worker per configured concurrent Group, plus the poll that wakes them, until the token is
    /// cancelled. Each worker serves itself: it repeats <see cref="ProcessNextAsync"/> for as long as that
    /// keeps finding work, and waits on the <see cref="WorkSignal"/> once it does not.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var workers = Enumerable.Range(0, _config.MaxConcurrentGroups)
            .Select(_ => RunWorkerAsync(cancellationToken))
            .Append(RunPollAsync(cancellationToken));

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that work may have appeared, so that idle workers stop waiting and look. Notifying while a
    /// notification is already pending is free and does nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is one of three peers, none of them privileged. The commit interceptor calls it when this
    /// process writes a message; <see cref="RunPollAsync"/> calls it on a timer; a
    /// <c>LISTEN</c>/<c>pg_notify</c> subscription would be the third, since notifying is in-process only
    /// and a commit on one application instance does not wake the workers of another.
    /// </para>
    /// <para>
    /// Polling is what actually guarantees delivery. Every other caller is a latency optimisation and is
    /// allowed to lose a notification: work nobody told the pool about is still picked up on the next
    /// poll, which is what makes a lost notification cost time rather than correctness.
    /// </para>
    /// </remarks>
    internal void NotifyWork()
    {
        _workSignal.Notify();
    }

    /// <summary>
    /// Handles at most one unit of work: the Head of whichever Group currently offers the oldest one.
    /// Nothing hands Groups to a worker - it claims one for itself, and the skip-locked semantics of that
    /// claim are what keep two workers off the same Group.
    /// </summary>
    /// <returns>
    /// Whether a message was claimed, and with it whether it is worth calling again right away. It is
    /// <see cref="ClaimResult.NothingOffered"/> when no Group offered anything - because nothing is
    /// unhandled, because every candidate Head is not yet visible, or because other workers hold the ones
    /// that are - and also when the claim itself failed, which is logged rather than thrown so that a worker
    /// survives it.
    /// </returns>
    internal async Task<ClaimResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        try
        {
            // use a separate scope & context for each claim
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProcessor<TEntity>>();

            return await processor.ProcessHeadAsync(scope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingError(ex);

            // treat a failed claim as no work rather than as a reason to try again immediately, so that a
            // database that is refusing connections is not hammered in a tight loop
            return ClaimResult.NothingOffered;
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // ProcessNextAsync reports anything short of a cancellation as "no work", so a worker keeps
            // serving itself across a failure rather than dying and leaving the pool one short
            if (await ProcessNextAsync(cancellationToken).ConfigureAwait(false) != ClaimResult.HeadClaimed)
            {
                await _workSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Notifies on a fixed cadence for as long as the pool runs, so that a wait ends even when nothing
    /// notified it. It ticks whether or not anyone is idle: a tick that nobody is waiting for leaves a
    /// token in the signal and costs the next worker to go idle one empty claim, which is cheaper than the
    /// shared idle count it would take to suppress.
    /// </summary>
    private async Task RunPollAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(_config.ProcessingDelayMilliseconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // delay before the first notification: a worker claims once before it ever waits, so a
                // notification at startup would release nobody
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutting down, which the loop condition sees on its own; ending the same way a worker
                // does keeps RunAsync completing successfully rather than faulting on every stop
                return;
            }

            NotifyWork();
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Error claiming or handling the next Head")]
    private partial void LogProcessingError(Exception exception);
}
