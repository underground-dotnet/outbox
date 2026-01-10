using System.Threading.Channels;

using Medallion.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal class ConcurrentProcessor<TEntity>(
    ILogger<ConcurrentProcessor<TEntity>> logger,
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration config,
    IDistributedLockProvider synchronizationProvider,
    FetchPartitions<TEntity> fetchPartitions
) where TEntity : class, IMessage
{
    private readonly ILogger<ConcurrentProcessor<TEntity>> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    internal readonly ServiceConfiguration Config = config;
    private readonly FetchPartitions<TEntity> _fetchPartitions = fetchPartitions;
    internal readonly Channel<string> Channel = System.Threading.Channels.Channel.CreateBounded<string>(new BoundedChannelOptions(10)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = CreateWorkers(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await StartProcessingRunAsync(cancellationToken);

            await Task.Delay(Config.ProcessingDelayMilliseconds, cancellationToken);
        }
    }

    internal async Task StartProcessingRunAsync(CancellationToken cancellationToken)
    {
        var partitions = await _fetchPartitions.ExecuteAsync(cancellationToken);

        foreach (var partition in partitions)
        {
            await Channel.Writer.WriteAsync(partition, cancellationToken);
        }
    }

    internal async Task<IEnumerable<Task>> CreateWorkers(CancellationToken cancellationToken)
    {
        return Enumerable.Range(0, Config.ParallelProcessingOfPartitions)
                    .Select(_ => CreatePartitionWorker(cancellationToken))
                    .ToArray();
    }

    internal async Task CreatePartitionWorker(CancellationToken cancellationToken)
    {
        await foreach (var partitionKey in Channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var messagesProcessed = await AcquireLockAndProcess(partitionKey, cancellationToken);

                if (messagesProcessed)
                {
                    // re-enqueue the partition for further processing, because there might be more messages
                    Channel.Writer.TryWrite(partitionKey);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not NoDbContextAssignedException)
            {
                _logger.LogError(ex, "Error processing partition {PartitionKey}", partitionKey);
            }
        }
    }

    private async Task<bool> AcquireLockAndProcess(string partitionKey, CancellationToken cancellationToken)
    {
        var lockKey = $"{typeof(TEntity)}-{partitionKey}";
        await using var handle = await synchronizationProvider.TryAcquireLockAsync(lockKey, cancellationToken: cancellationToken);
        if (handle is null)
        {
            // another instance is already processing the partition
            return false;
        }

        ProcessingPartitionStarted();

        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();
        var messagesProcessed = await processor.ProcessPartitionBatchAsync(partitionKey, Config.BatchSize, scope, cancellationToken);

        ProcessingPartitionCompleted(messagesProcessed);

        return messagesProcessed;
    }

    protected virtual void ProcessingPartitionStarted()
    {
    }

    protected virtual void ProcessingPartitionCompleted(bool messagesProcessed)
    {
    }
}
