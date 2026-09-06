using System.Threading.Channels;

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

    /// <summary>
    /// The wake-up mechanism behind the worker pool: idle workers wait on it, and anything that knows work
    /// may have appeared writes to it. It carries no information beyond "look again", and no guarantee that
    /// looking will find anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two details of it are load-bearing rather than incidental, and both read as mistakes to someone who
    /// does not know what they are for.
    /// </para>
    /// <para>
    /// <b>A notification releases every waiter, not one.</b> <see cref="WaitForWorkAsync"/> awaits
    /// <c>WaitToReadAsync</c>, which completes for all waiters, and only then drains the token. Whichever
    /// worker wins that race is immaterial, because they have all been released by the time it is drained.
    /// Releasing one instead would leave a commit that arrives at an idle pool served by a single worker
    /// handling every Group serially until the next poll.
    /// </para>
    /// <para>
    /// <b>The notification is buffered, so it cannot be lost.</b> A <see cref="NotifyWork"/> that lands
    /// between a worker finding no work and that worker starting to wait leaves the token sitting in the
    /// channel, and the wait returns immediately. A plain pulse would drop that notification and cost a full
    /// poll delay. The channel is bounded at one with <see cref="BoundedChannelFullMode.DropWrite"/> because
    /// the token carries no information: a second notification arriving before the first is consumed says
    /// nothing the first did not. Dropping it loses nothing either, because a token is only pending while
    /// some worker's next claim has yet to start, and that claim sees whatever the dropped notification was
    /// reporting.
    /// </para>
    /// </remarks>
    private readonly Channel<byte> _workSignal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    /// <summary>
    /// Runs one worker per configured concurrent Group, plus the poll that wakes them, until the token is
    /// cancelled. Each worker serves itself: it repeats <see cref="ProcessNextAsync"/> for as long as that
    /// keeps finding work, and waits on the work signal once it does not.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var workers = Enumerable.Range(0, _config.MaxConcurrentGroups)
            .Select(_ => RunWorkerAsync(cancellationToken))
            .Append(RunPollAsync(cancellationToken));

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that work may have appeared, releasing every worker currently waiting. It never blocks and
    /// never fails: a notification that arrives while one is already pending is dropped, because the two say
    /// the same thing.
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
        _workSignal.Writer.TryWrite(0);
    }

    /// <summary>
    /// Waits until <see cref="NotifyWork"/> is called. Returns rather than throwing when
    /// <paramref name="cancellationToken"/> is cancelled; the worker loop decides what a cancellation means.
    /// Nothing here gives up on its own - a wait ends because somebody notified, or because the application
    /// is shutting down.
    /// </summary>
    internal async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _workSignal.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);

            // take the token so that the next wait blocks again
            _workSignal.Reader.TryRead(out _);
        }
        catch (OperationCanceledException)
        {
            // the application is shutting down, which the worker loop sees on its own token
        }
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
                await WaitForWorkAsync(cancellationToken).ConfigureAwait(false);
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
