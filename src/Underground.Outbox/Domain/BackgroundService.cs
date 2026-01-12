using Microsoft.Extensions.Hosting;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class BackgroundService<TEntity>(
    ConcurrentProcessor<TEntity> processor
) : BackgroundService where TEntity : class, IMessage
{
    private readonly ConcurrentProcessor<TEntity> _processor = processor ?? throw new ArgumentNullException(nameof(processor));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TODO: if user wants to process on only one machine
        // var lockKey = $"{typeof(TEntity)}-{partitionKey}";
        // await using var handle = await synchronizationProvider.TryAcquireLockAsync(lockKey, cancellationToken: cancellationToken);
        // if (handle is null)
        // {
        //     // another instance is already processing the partition
        //     return false;
        // }

        await _processor.StartAsync(stoppingToken);
    }
}
