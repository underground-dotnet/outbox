using Medallion.Threading;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class BackgroundService<TEntity>(
    Processor<TEntity> processor,
    IDistributedLockProvider synchronizationProvider,
    ILogger<BackgroundService<TEntity>> logger,
    ServiceConfiguration serviceConfiguration
) : BackgroundService where TEntity : class, IMessage
{
    private readonly IDistributedLock _distributedLock = synchronizationProvider.CreateLock($"{typeof(TEntity)}BackgroundServiceLock");
    private readonly Processor<TEntity> _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ServiceConfiguration _serviceConfiguration = serviceConfiguration ?? throw new ArgumentNullException(nameof(serviceConfiguration));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var handle = await _distributedLock.TryAcquireAsync(cancellationToken: stoppingToken);

            if (handle is not null)
            {
                await StartProcessingAsync(stoppingToken);
            }
            else
            {
                // another instance is already processing the outbox
                // _logger.LogInformation("Another instance is already processing the outbox. Skipping this run.");
                await Task.Delay(30_000, stoppingToken);
            }
        }
    }

    private async Task StartProcessingAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _processor.ProcessAsync();
            await Task.Delay(_serviceConfiguration.ProcessingDelayMilliseconds, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not NoDbContextAssignedException)
        {
            _logger.LogError(ex, "BackgroundService Error");
            await Task.Delay(3000, stoppingToken);
        }
    }
}
