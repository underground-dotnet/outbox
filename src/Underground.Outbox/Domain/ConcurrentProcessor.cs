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

    // used to trigger processing runs, making sure only a limited number of runs can be queued
    private readonly Channel<int> _triggerChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    // contains groups to be processed
    private readonly Channel<string> _groupsChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(20)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    /// <summary>
    /// Runs one worker per configured concurrent Group until the token is cancelled. Each worker repeats
    /// <see cref="ProcessNextAsync"/> and waits for new work whenever there is none.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        // handle whatever is already waiting in the database when the application starts
        ScheduleProcessingRun();

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
    /// Handles at most one unit of work: one batch of messages of the next waiting Group. When no Group
    /// is waiting, the Groups holding unprocessed messages are discovered first, but only if a run was
    /// scheduled through <see cref="ScheduleProcessingRun"/>. A Group whose messages keep failing is
    /// therefore retried once per run rather than continuously, which is what stands in for a retry
    /// backoff until there is one.
    /// </summary>
    /// <returns>
    /// A boolean indicating whether a Group was taken, and with it whether it is worth calling again right away.
    /// It is <c>false</c> only when no Group was waiting and none could be discovered. A Group that turns out to
    /// hold no messages - because another worker holds it, or because it was emptied by the previous batch -
    /// still counts as taken.
    /// </returns>
    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var groupKey = await TakeNextGroupAsync(cancellationToken).ConfigureAwait(false);

        if (groupKey is null)
        {
            return false;
        }

        try
        {
            // locking right now is performed through the `FOR UPDATE NOWAIT` clause in `FetchMessages`,
            // so a Group another worker is holding simply yields no messages.
            // use separate scope & context for each group
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();
            var batchCompleted = await processor.ProcessMessagesAsync(groupKey, _config.BatchSize, scope, cancellationToken).ConfigureAwait(false);

            if (batchCompleted)
            {
                // re-enqueue the group for further processing, because there might be more messages
                _groupsChannel.Writer.TryWrite(groupKey);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogGroupProcessingError(groupKey, ex);
        }

        return true;
    }

    private async Task<string?> TakeNextGroupAsync(CancellationToken cancellationToken)
    {
        if (_groupsChannel.Reader.TryRead(out var groupKey))
        {
            return groupKey;
        }

        if (!_triggerChannel.Reader.TryRead(out _))
        {
            return null;
        }

        await DiscoverGroupsAsync(cancellationToken).ConfigureAwait(false);

        return _groupsChannel.Reader.TryRead(out groupKey) ? groupKey : null;
    }

    private async Task DiscoverGroupsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var groups = await scope.ServiceProvider.GetRequiredService<FetchGroups<TEntity>>().ExecuteAsync(cancellationToken).ConfigureAwait(false);

            foreach (var groupKey in groups)
            {
                _groupsChannel.Writer.TryWrite(groupKey);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFetchGroupsError(ex);
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool groupTaken;

            try
            {
                groupTaken = await ProcessNextAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWorkerFailed(ex);
                groupTaken = false;
            }

            if (!groupTaken)
            {
                await WaitForWorkAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Waits until another worker queues a Group or a run is scheduled, and gives up after the configured
    /// processing delay so that work nothing pushed to us is still picked up.
    /// </summary>
    private async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(_config.ProcessingDelayMilliseconds);

        var groupQueued = _groupsChannel.Reader.WaitToReadAsync(wait.Token).AsTask();
        var runScheduled = _triggerChannel.Reader.WaitToReadAsync(wait.Token).AsTask();

        await Task.WhenAny(groupQueued, runScheduled).ConfigureAwait(false);

        // release and observe the wait that lost the race, so that it is not left running unobserved
        await wait.CancelAsync().ConfigureAwait(false);
        await AwaitCancelledWaitAsync(groupQueued).ConfigureAwait(false);
        await AwaitCancelledWaitAsync(runScheduled).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (groupQueued.IsCanceled && runScheduled.IsCanceled)
        {
            // the processing delay elapsed without anything waking us up, so poll for work
            ScheduleProcessingRun();
        }
    }

    private static async Task AwaitCancelledWaitAsync(Task wait)
    {
        try
        {
            await wait.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // this wait lost the race and was cancelled on purpose
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Worker failed with an exception")]
    private partial void LogWorkerFailed(Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Error fetching groups for processing")]
    private partial void LogFetchGroupsError(Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Error processing group {GroupKey}")]
    private partial void LogGroupProcessingError(string groupKey, Exception exception);
}
