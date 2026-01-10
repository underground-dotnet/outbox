using System.Threading.Channels;

using Medallion.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class BackgroundService<TEntity>(
    ConcurrentProcessor<TEntity> processor
// IDistributedLockProvider synchronizationProvider,
// ILogger<BackgroundService<TEntity>> logger,
// ServiceConfiguration serviceConfiguration,
// IServiceScopeFactory scopeFactory
) : BackgroundService where TEntity : class, IMessage
{
    // private readonly IDistributedLock _distributedLock = synchronizationProvider.CreateLock($"{typeof(TEntity)}BackgroundServiceLock");
    private readonly ConcurrentProcessor<TEntity> _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    // private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    // private readonly ServiceConfiguration _config = serviceConfiguration ?? throw new ArgumentNullException(nameof(serviceConfiguration));
    // private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    // private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(10)
    // {
    //     FullMode = BoundedChannelFullMode.DropWrite,
    //     SingleReader = false,
    //     SingleWriter = false
    // });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _processor.StartAsync(stoppingToken);
        // var workers = Enumerable.Range(0, _config.ParallelProcessingOfPartitions)
        //     .Select(_ => ProcessPartitionWorker(stoppingToken))
        //     .ToArray();

        // while (!stoppingToken.IsCancellationRequested)
        // {
        //     // var partitions = await fetchPartitions.ExecuteAsync(stoppingToken);

        //     // foreach (var partition in partitions)
        //     // {
        //     //     await _channel.Writer.WriteAsync(partition, stoppingToken);
        //     // }

        //     await _processor.FetchPartitionsAndProcess(stoppingToken);

        //     await Task.Delay(_config.ProcessingDelayMilliseconds, stoppingToken);

        //     //     await using var handle = await _distributedLock.TryAcquireAsync(cancellationToken: stoppingToken);

        //     //     if (handle is not null)
        //     //     {
        //     //         await StartProcessingAsync(stoppingToken);
        //     //     }
        //     //     else
        //     //     {
        //     //         // another instance is already processing the outbox
        //     //         // _logger.LogInformation("Another instance is already processing the outbox. Skipping this run.");
        //     //         await Task.Delay(30_000, stoppingToken);
        //     //     }
        // }
    }

    // private async Task ProcessPartitionWorker(CancellationToken cancellationToken)
    // {
    //     await foreach (var partitionKey in _channel.Reader.ReadAllAsync(cancellationToken))
    //     {
    //         try
    //         {
    //             var @lock = synchronizationProvider.CreateLock($"{typeof(TEntity)}-{partitionKey}");
    //             await using var handle = await @lock.TryAcquireAsync(cancellationToken: cancellationToken);

    //             if (handle is null)
    //             {
    //                 // another instance is already processing the outbox
    //                 return;
    //             }

    //             using var scope = _scopeFactory.CreateScope();
    //             var processor = scope.ServiceProvider.GetRequiredService<Processor<TEntity>>();
    //             var messagesProcessed = await processor.ProcessPartitionBatchAsync(partitionKey, _config.BatchSize, cancellationToken);

    //             if (messagesProcessed)
    //             {
    //                 // re-enqueue the partition for further processing, because there might be more messages
    //                 _channel.Writer.TryWrite(partitionKey);
    //             }
    //         }
    //         catch (Exception ex) when (ex is not OperationCanceledException && ex is not NoDbContextAssignedException)
    //         {
    //             _logger.LogError(ex, "Error processing partition {PartitionKey}", partitionKey);
    //         }
    //     }
    // }

    // private async Task StartProcessingAsync(CancellationToken stoppingToken)
    // {
    //     try
    //     {
    //         await _processor.ProcessAsync(stoppingToken);
    //         await Task.Delay(_serviceConfiguration.ProcessingDelayMilliseconds, stoppingToken);
    //     }
    //     catch (Exception ex) when (ex is not OperationCanceledException && ex is not NoDbContextAssignedException)
    //     {
    //         _logger.LogError(ex, "BackgroundService Error");
    //         await Task.Delay(3000, stoppingToken);
    //     }
    // }
}
