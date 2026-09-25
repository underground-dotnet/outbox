using Microsoft.Extensions.Hosting;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;

namespace Underground.Outbox.Domain;

internal sealed class BackgroundService<TEntity> : BackgroundService where TEntity : class, IMessage
{
    private readonly ConcurrentProcessor<TEntity> _processor;

    // taking the registry builds it as the host starts, so a CompetingHandlersException fails startup
    // instead of being logged by every worker on every claim
    public BackgroundService(ConcurrentProcessor<TEntity> processor, HandlerRegistry<TEntity> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TODO: if user wants to process on only one machine
        // var lockKey = $"{typeof(TEntity)}-{groupKey}";
        // await using var handle = await synchronizationProvider.TryAcquireLockAsync(lockKey, cancellationToken: cancellationToken);
        // if (handle is null)
        // {
        //     // another instance is already processing the group
        //     return false;
        // }

        await _processor.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
