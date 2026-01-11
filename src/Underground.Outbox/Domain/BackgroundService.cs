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
        await _processor.StartAsync(stoppingToken);
    }
}
