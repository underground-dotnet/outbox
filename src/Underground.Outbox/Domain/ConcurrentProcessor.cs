using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal class ConcurrentProcessor<TEntity>(
    ILogger<ConcurrentProcessor<TEntity>> logger,
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration config
) where TEntity : class, IMessage
{
    private readonly ILogger<ConcurrentProcessor<TEntity>> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ServiceConfiguration _config = config;

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
        // TODO: monitor workers?
        _ = await CreateWorkers(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            ScheduleProcessingRun();
            await Task.Delay(_config.ProcessingDelayMilliseconds, cancellationToken);
        }
    }

    internal void ScheduleProcessingRun()
    {
        _triggerChannel.Writer.TryWrite(1);
    }

    private async Task<IEnumerable<Task>> CreateWorkers(CancellationToken cancellationToken)
    {
        var triggerWorker = CreateTriggerWorker(cancellationToken);

        var partitionsWorkers = Enumerable.Range(0, _config.ParallelProcessingOfPartitions)
                    .Select(_ => CreatePartitionWorker(cancellationToken))
                    .ToArray();

        return [.. partitionsWorkers, triggerWorker];
    }

    private async Task CreateTriggerWorker(CancellationToken cancellationToken)
    {
        await foreach (var _ in _triggerChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var partitions = await scope.ServiceProvider.GetRequiredService<FetchPartitions<TEntity>>().ExecuteAsync(cancellationToken);

                foreach (var partition in partitions)
                {
                    await _partitionsChannel.Writer.WriteAsync(partition, cancellationToken);
                }

                if (!partitions.Any())
                {
                    NoMessagesForProcessingFound();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error fetching partitions for processing");
                NoMessagesForProcessingFound();
            }
        }
    }

    private async Task CreatePartitionWorker(CancellationToken cancellationToken)
    {
        await foreach (var partitionKey in _partitionsChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var messagesProcessed = await AcquireLockAndProcess(partitionKey, cancellationToken);

                if (messagesProcessed)
                {
                    // re-enqueue the partition for further processing, because there might be more messages
                    _partitionsChannel.Writer.TryWrite(partitionKey);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing partition {PartitionKey}", partitionKey);
            }
        }
    }

    private async Task<bool> AcquireLockAndProcess(string partitionKey, CancellationToken cancellationToken)
    {
        // use separate scope & context for each partition
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();
        return await processor.ProcessMessagesAsync(partitionKey, _config.BatchSize, scope, cancellationToken);
    }

    protected virtual void NoMessagesForProcessingFound()
    {
        // only used to improve test setup with async processes
    }
}
