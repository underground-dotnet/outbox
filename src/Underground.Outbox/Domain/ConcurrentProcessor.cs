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
    /// Wake-up signal for idle workers. Carries no information beyond "look again".
    /// </summary>
    /// <remarks>
    /// Two properties are load-bearing: a notification releases every waiter (<c>WaitToReadAsync</c>
    /// completes for all of them before the token is drained), and it is buffered, so one that lands
    /// just before a worker starts waiting is not lost. Bounded at one with
    /// <see cref="BoundedChannelFullMode.DropWrite"/>: a second token says nothing the first did not.
    /// </remarks>
    private readonly Channel<byte> _workSignal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    /// <summary>
    /// Runs one worker per configured concurrent Group, plus the poll that wakes them, until cancelled.
    /// Each worker claims for itself and waits on the work signal once nothing is offered.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var workers = Enumerable.Range(0, _config.MaxConcurrentGroups)
            .Select(_ => RunWorkerAsync(cancellationToken))
            .Append(RunPollAsync(cancellationToken));

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that work may have appeared, releasing every waiting worker. Never blocks, never fails.
    /// </summary>
    /// <remarks>
    /// Polling is what guarantees delivery; the commit interceptor calling this is only a latency
    /// optimisation, and a lost notification therefore costs time rather than correctness.
    /// </remarks>
    internal void NotifyWork()
    {
        _workSignal.Writer.TryWrite(0);
    }

    /// <summary>
    /// Waits until <see cref="NotifyWork"/> is called. Returns rather than throwing on cancellation; the
    /// worker loop decides what that means.
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
    /// Handles at most one unit of work: the HeadMessage of whichever Group offers the oldest one. The
    /// skip-locked claim is what keeps two workers off the same Group.
    /// </summary>
    /// <returns>
    /// Whether a message was claimed, and with it whether it is worth calling again right away. A failed
    /// claim is logged and reported as <see cref="ClaimResult.NothingOffered"/> so a worker survives it.
    /// </returns>
    internal async Task<ClaimResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        try
        {
            // use a separate scope & context for each claim
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProcessor<TEntity>>();

            return await processor.TryProcessHeadMessageAsync(scope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingError(ex);

            // no work rather than an immediate retry, so a database refusing connections is not hammered
            return ClaimResult.NothingOffered;
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // ProcessNextAsync reports anything short of a cancellation as "no work", so a worker
            // survives a failure rather than leaving the pool one short
            if (await ProcessNextAsync(cancellationToken).ConfigureAwait(false) != ClaimResult.HeadMessageClaimed)
            {
                await WaitForWorkAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Notifies on a fixed cadence, so a wait ends even when nothing notified it. It ticks whether or not
    /// anyone is idle; suppressing that would cost a shared idle count for the sake of one empty claim.
    /// </summary>
    private async Task RunPollAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(_config.ProcessingDelayMilliseconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // delay first: a worker claims before it ever waits, so a startup tick releases nobody
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutting down; returning rather than throwing keeps RunAsync from faulting on every stop
                return;
            }

            NotifyWork();
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Error claiming or handling the next HeadMessage")]
    private partial void LogProcessingError(Exception exception);
}
