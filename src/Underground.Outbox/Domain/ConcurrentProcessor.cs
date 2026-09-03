using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal partial class ConcurrentProcessor<TEntity>(
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
        SingleReader = true,
        SingleWriter = false
    });

    // contains groups to be processed
    private readonly Channel<string> _groupsChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(20)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    // called only on startup in the BackgroundWorker
    internal virtual async Task StartAsync(CancellationToken cancellationToken)
    {
        CreateWorkers(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            ScheduleProcessingRun();
            await Task.Delay(_config.ProcessingDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    internal void ScheduleProcessingRun()
    {
        _triggerChannel.Writer.TryWrite(1);
    }

    protected void CreateWorkers(CancellationToken cancellationToken)
    {
        var triggerWorker = CreateTriggerWorker(cancellationToken);

        var groupWorkers = Enumerable.Range(0, _config.MaxConcurrentGroups)
                    .Select(_ => CreateGroupWorker(cancellationToken))
                    .ToArray();

        List<Task> tasks = [.. groupWorkers, triggerWorker];
        tasks.ForEach(t =>
            // since we are not awaiting the tasks here, we need to log exceptions manually to avoid unobserved task exceptions
            _ = t.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        LogWorkerFailed(t.Exception);
                    }
                },
                TaskContinuationOptions.OnlyOnFaulted
            )
        );
    }

    private async Task CreateTriggerWorker(CancellationToken cancellationToken)
    {
        await foreach (var _ in _triggerChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var groups = await scope.ServiceProvider.GetRequiredService<FetchGroups<TEntity>>().ExecuteAsync(cancellationToken).ConfigureAwait(false);

                foreach (var groupKey in groups)
                {
                    await _groupsChannel.Writer.WriteAsync(groupKey, cancellationToken).ConfigureAwait(false);
                }

                if (!groups.Any())
                {
                    NoMessagesForProcessingFound();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFetchGroupsError(ex);
                NoMessagesForProcessingFound();
            }
        }
    }

    private async Task CreateGroupWorker(CancellationToken cancellationToken)
    {
        await foreach (var groupKey in _groupsChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var messagesProcessed = await AcquireLockAndProcess(groupKey, cancellationToken).ConfigureAwait(false);

                if (messagesProcessed)
                {
                    // re-enqueue the group for further processing, because there might be more messages
                    _groupsChannel.Writer.TryWrite(groupKey);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogGroupProcessingError(groupKey, ex);
            }
        }
    }

    // locking right not is performed through the `FOR UPDATE NOWAIT` clause in `FetchMessages`
    private async Task<bool> AcquireLockAndProcess(string groupKey, CancellationToken cancellationToken)
    {
        // use separate scope & context for each group
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();
        return await processor.ProcessMessagesAsync(groupKey, _config.BatchSize, scope, cancellationToken).ConfigureAwait(false);
    }

    protected virtual void NoMessagesForProcessingFound()
    {
        // only used to improve test setup with async processes
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
