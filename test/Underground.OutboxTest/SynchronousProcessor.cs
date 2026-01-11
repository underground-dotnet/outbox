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
    private TaskCompletionSource<bool>? _processingTCS = null;
    private readonly Lock _lock = new();

    protected override void NoMessagesForProcessingFound()
    {
        lock (_lock)
        {
            _processingTCS?.SetResult(true);
        }
    }

    internal Task ProcessAndWaitAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_processingTCS is null)
            {
                // only executed on first call
                _processingTCS = new TaskCompletionSource<bool>();
                _ = StartAsync(cancellationToken);
            }
            else if (_processingTCS is not null && !_processingTCS.Task.IsCompleted)
            {
                // still running
                return _processingTCS.Task;
            }
            else
            {
                // completed, start new run
                _processingTCS = new TaskCompletionSource<bool>();
                ScheduleProcessingRun();
            }
        }

        return _processingTCS.Task;
    }
}
