using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class CleanupBackgroundService<TEntity>(
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration<TEntity> config,
    ILogger<CleanupBackgroundService<TEntity>> logger
) : BackgroundService where TEntity : class, IMessage
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(config.CleanupDelaySeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            await PerformDelete(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PerformDelete(CancellationToken stoppingToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var cleanup = scope.ServiceProvider.GetRequiredService<DeleteProcessedMessages<TEntity>>();
                var deletedCount = await cleanup.ExecuteAsync(stoppingToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Deleted {DeletedCount} processed {MessageType} messages.",
                    deletedCount,
                    typeof(TEntity).Name
                );
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Cleanup failed for processed {MessageType} messages.",
                typeof(TEntity).Name
            );
        }
    }
}
