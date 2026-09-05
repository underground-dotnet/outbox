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

    // used to wake idle workers, making sure only a limited number of runs can be queued
    private readonly Channel<int> _triggerChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    /// <summary>
    /// Runs one worker per configured concurrent Group until the token is cancelled. Each worker serves
    /// itself: it repeats <see cref="ProcessNextAsync"/> for as long as that keeps finding work, and waits
    /// for a trigger or the poll delay once it does not.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var workers = Enumerable.Range(0, _config.MaxConcurrentGroups)
            .Select(_ => RunWorkerAsync(cancellationToken));

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the workers to look for new work. A run that is already scheduled is not scheduled twice.
    /// </summary>
    internal void ScheduleProcessingRun()
    {
        _triggerChannel.Writer.TryWrite(1);
    }

    /// <summary>
    /// Handles at most one unit of work: the Head of whichever Group currently offers the oldest one.
    /// Nothing hands Groups to a worker - it claims one for itself, and the skip-locked semantics of that
    /// claim are what keep two workers off the same Group.
    /// </summary>
    /// <returns>
    /// A boolean indicating whether a message was claimed, and with it whether it is worth calling again
    /// right away. It is <c>false</c> when no Group offered anything - because nothing is unhandled, because
    /// every candidate Head is not yet visible, or because other workers hold the ones that are - and also
    /// when the claim itself failed, which is logged rather than thrown so that a worker survives it.
    /// </returns>
    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        try
        {
            // use a separate scope & context for each claim
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();

            return await processor.ProcessHeadAsync(scope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingError(ex);

            // treat a failed claim as no work rather than as a reason to try again immediately, so that a
            // database that is refusing connections is not hammered in a tight loop
            return false;
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // ProcessNextAsync reports anything short of a cancellation as "no work", so a worker keeps
            // serving itself across a failure rather than dying and leaving the pool one short
            if (!await ProcessNextAsync(cancellationToken).ConfigureAwait(false))
            {
                await WaitForWorkAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Waits until a processing run is scheduled, and gives up after the configured processing delay so
    /// that work nothing pushed to us is still picked up.
    /// </summary>
    private async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(_config.ProcessingDelayMilliseconds);

        try
        {
            // waiting to read releases every idle worker rather than only the one that ends up taking the
            // token, so a single commit puts the whole pool back to work
            await _triggerChannel.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);

            // take the token so that the next wait blocks again; whichever worker wins the race is
            // immaterial, because they have all been released by this point
            _triggerChannel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException)
        {
            // either the processing delay elapsed, which is itself a reason to look for work, or the
            // application is shutting down, which the check below reports
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Error claiming or handling the next Head")]
    private partial void LogProcessingError(Exception exception);
}
