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
    /// Runs one worker per configured concurrent Group until the token is cancelled. Each worker serves
    /// itself: it repeats <see cref="ProcessNextAsync"/> for as long as that keeps finding work, and waits
    /// on the <see cref="WorkSignal"/> or the poll delay once it does not.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var workers = Enumerable.Range(0, _config.MaxConcurrentGroups)
            .Select(_ => RunWorkerAsync(cancellationToken));

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that work may have appeared, so that idle workers stop waiting and look. Notifying while a
    /// notification is already pending is free and does nothing.
    /// </summary>
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
            var processor = scope.ServiceProvider.GetRequiredService<IProcessor<TEntity>>();

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
                await _workSignal
                    .WaitAsync(TimeSpan.FromMilliseconds(_config.ProcessingDelayMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Error claiming or handling the next Head")]
    private partial void LogProcessingError(Exception exception);
}
