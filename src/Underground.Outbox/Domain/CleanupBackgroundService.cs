using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed partial class CleanupBackgroundService<TEntity>(
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

                LogDeletedMessages(deletedCount, typeof(TEntity).Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCleanupFailed(typeof(TEntity).Name, ex);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Deleted {DeletedCount} processed {MessageType} messages.")]
    private partial void LogDeletedMessages(int deletedCount, string messageType);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Cleanup failed for processed {MessageType} messages.")]
    private partial void LogCleanupFailed(string messageType, Exception exception);
}
