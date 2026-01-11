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
    IDistributedLockProvider synchronizationProvider
) where TEntity : class, IMessage
{
    private readonly ILogger<ConcurrentProcessor<TEntity>> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ServiceConfiguration _config = config;
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(10)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = await CreateWorkers(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await StartProcessingRunAsync(cancellationToken);

            await Task.Delay(_config.ProcessingDelayMilliseconds, cancellationToken);
        }
    }

    // TODO: some sort of locking? or second channel? When this method gets called like from a dbcontext interceptor then it will be a lot of calls and can result in some wait time.
    internal async Task StartProcessingRunAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var partitions = await scope.ServiceProvider.GetRequiredService<FetchPartitions<TEntity>>().ExecuteAsync(cancellationToken);

        foreach (var partition in partitions)
        {
            await _channel.Writer.WriteAsync(partition, cancellationToken);
        }

        if (!partitions.Any())
        {
            ProcessingPartitionCompleted(false);
        }
    }

    internal async Task<IEnumerable<Task>> CreateWorkers(CancellationToken cancellationToken)
    {
        return Enumerable.Range(0, _config.ParallelProcessingOfPartitions)
                    .Select(_ => CreatePartitionWorker(cancellationToken))
                    .ToArray();
    }

    private async Task CreatePartitionWorker(CancellationToken cancellationToken)
    {
        await foreach (var partitionKey in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var messagesProcessed = await AcquireLockAndProcess(partitionKey, cancellationToken);

                if (messagesProcessed)
                {
                    // re-enqueue the partition for further processing, because there might be more messages
                    _channel.Writer.TryWrite(partitionKey);
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

        // use separate scope & context for each partition
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();
        var messagesProcessed = await processor.ProcessMessagesAsync(partitionKey, _config.BatchSize, scope, cancellationToken);

        ProcessingPartitionCompleted(messagesProcessed);

        return messagesProcessed;
    }

    protected virtual void ProcessingPartitionStarted()
    {
        // only used to improve test setup with async processes
    }

    protected virtual void ProcessingPartitionCompleted(bool messagesProcessed)
    {

    }
}
