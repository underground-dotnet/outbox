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
    private readonly Channel<int> _triggerChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(2)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    // contains partitions to be processed
    private readonly Channel<string> _partitionsChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(20)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    // called only on startup in the BackgroundWorker
    internal async Task StartAsync(CancellationToken cancellationToken)
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

    private void CreateWorkers(CancellationToken cancellationToken)
    {
        var triggerWorker = CreateTriggerWorker(cancellationToken);

        var partitionsWorkers = Enumerable.Range(0, _config.ParallelProcessingOfPartitions)
                    .Select(_ => CreatePartitionWorker(cancellationToken))
                    .ToArray();

        List<Task> tasks = [.. partitionsWorkers, triggerWorker];
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
                var partitions = await scope.ServiceProvider.GetRequiredService<FetchPartitions<TEntity>>().ExecuteAsync(cancellationToken).ConfigureAwait(false);

                foreach (var partition in partitions)
                {
                    await _partitionsChannel.Writer.WriteAsync(partition, cancellationToken).ConfigureAwait(false);
                }

                if (!partitions.Any())
                {
                    NoMessagesForProcessingFound();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFetchPartitionsError(ex);
                NoMessagesForProcessingFound();
            }
        }
    }

    private async Task CreatePartitionWorker(CancellationToken cancellationToken)
    {
        await foreach (var partitionKey in _partitionsChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var messagesProcessed = await AcquireLockAndProcess(partitionKey, cancellationToken).ConfigureAwait(false);

                if (messagesProcessed)
                {
                    // re-enqueue the partition for further processing, because there might be more messages
                    _partitionsChannel.Writer.TryWrite(partitionKey);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPartitionProcessingError(partitionKey, ex);
            }
        }
    }

    // locking right not is performed through the `FOR UPDATE NOWAIT` clause in `FetchMessages`
    private async Task<bool> AcquireLockAndProcess(string partitionKey, CancellationToken cancellationToken)
    {
        // use separate scope & context for each partition
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();
        return await processor.ProcessMessagesAsync(partitionKey, _config.BatchSize, scope, cancellationToken).ConfigureAwait(false);
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
        Message = "Error fetching partitions for processing")]
    private partial void LogFetchPartitionsError(Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Error processing partition {PartitionKey}")]
    private partial void LogPartitionProcessingError(string partitionKey, Exception exception);
}
