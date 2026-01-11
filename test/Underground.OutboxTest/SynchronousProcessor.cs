using Medallion.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest;

internal sealed class SynchronousProcessor<TEntity>(
    ILogger<ConcurrentProcessor<TEntity>> logger,
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration config,
    IDistributedLockProvider synchronizationProvider
) : ConcurrentProcessor<TEntity>(logger, scopeFactory, config, synchronizationProvider) where TEntity : class, IMessage
{

    private int _activePartitions = 0;
    private TaskCompletionSource<bool>? _processingTCS = null;
    private readonly Lock _lock = new();

    protected override void ProcessingPartitionStarted()
    {
        Interlocked.Increment(ref _activePartitions);
    }

    protected override void ProcessingPartitionCompleted(bool messagesProcessed)
    {
        Interlocked.Decrement(ref _activePartitions);

        if (!messagesProcessed && _activePartitions <= 0)
        {
            lock (_lock)
            {
                _processingTCS?.SetResult(true);
            }
        }
    }

    internal Task ProcessAndWaitAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_processingTCS is not null && !_processingTCS.Task.IsCompleted)
            {
                // still running
                return _processingTCS.Task;
            }

            _processingTCS = new TaskCompletionSource<bool>();
            _activePartitions = 0;
            _ = CreateWorkers(cancellationToken);
        }

        _ = StartProcessingRunAsync(cancellationToken);
        return _processingTCS.Task;
    }
}
