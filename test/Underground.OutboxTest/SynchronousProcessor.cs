using Medallion.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest;

// internal static class ConcurrentProcessor
// {
//     extension<TEntity>(ConcurrentProcessor<TEntity> processor) where TEntity : class, IMessage
//     {
//         internal async Task ProcessAndWaitAsync(CancellationToken cancellationToken)
//         {
//             processor.ProcessingTCS = new TaskCompletionSource<bool>();
//             var workers = processor.CreateWorkers(cancellationToken);

//             await processor.StartProcessingRunAsync(cancellationToken);
//             // processor.Channel.Writer.Complete();

//             // await Task.WhenAll(workers);

//             await processor.ProcessingTCS.Task;

//         }
//     }
// }

internal class SynchronousProcessor<TEntity>(
    ILogger<ConcurrentProcessor<TEntity>> logger,
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration config,
    IDistributedLockProvider synchronizationProvider,
    FetchPartitions<TEntity> fetchPartitions
) : ConcurrentProcessor<TEntity>(logger, scopeFactory, config, synchronizationProvider, fetchPartitions) where TEntity : class, IMessage
{

    private int _activePartitions = 0;
    private TaskCompletionSource<bool>? _processingTCS = null;

    protected override void ProcessingPartitionStarted()
    {
        Interlocked.Increment(ref _activePartitions);
    }

    protected override void ProcessingPartitionCompleted(bool messagesProcessed)
    {
        Interlocked.Decrement(ref _activePartitions);

        if (!messagesProcessed && _activePartitions == 0)
        {
            _processingTCS?.SetResult(true);
        }
    }

    internal async Task ProcessAndWaitAsync(CancellationToken cancellationToken)
    {
        // lock (_lock)
        // {
        //     if (_processingTCS is not null && !_processingTCS.Task.IsCompleted)
        //     {
        //         // still running
        //         return _processingTCS.Task;
        //     }

        //     _processingTCS = new TaskCompletionSource<bool>();
        //     _ = ProcessAsync(cancellationToken);

        //     return _processingTCS.Task;
        // }

        // var workers = Enumerable.Range(0, Config.ParallelProcessingOfPartitions)
        //             .Select(_ => ProcessPartitionWorker(cancellationToken))
        //             .ToArray();

        // await StartProcessingRunAsync(cancellationToken);
        // Channel.Writer.Complete();

        // await Task.WhenAll(workers);

        _processingTCS = new TaskCompletionSource<bool>();
        _ = CreateWorkers(cancellationToken);

        await StartProcessingRunAsync(cancellationToken);

        await _processingTCS.Task;
    }
}
