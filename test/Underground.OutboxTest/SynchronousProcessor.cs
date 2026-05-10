using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest;

internal sealed class SynchronousProcessor<TEntity>(
    ILogger<ConcurrentProcessor<TEntity>> logger,
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration<TEntity> config
) : ConcurrentProcessor<TEntity>(logger, scopeFactory, config) where TEntity : class, IMessage
{
    private TaskCompletionSource<bool>? _processingTCS;
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
                var currentTCS = new TaskCompletionSource<bool>();
                _processingTCS = currentTCS;
                _ = StartAsync(cancellationToken).ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            lock (_lock)
                            {
                                currentTCS.SetException(t.Exception!);
                            }
                        }
                    },
                    TaskContinuationOptions.OnlyOnFaulted
                );
            }
            else if (_processingTCS.Task.IsCompleted)
            {
                // completed, start new run
                _processingTCS = new TaskCompletionSource<bool>();
                ScheduleProcessingRun();
            }

            return _processingTCS.Task;
        }
    }
}
