using Medallion.Threading;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class OutboxBackgroundService : BackgroundService
{
    private readonly IDistributedLock _distributedLock;
    private readonly OutboxProcessor _processor;
    private readonly IServiceScope _scope;

    private readonly ILogger _logger;

    public OutboxBackgroundService(OutboxServiceConfiguration config, IServiceScopeFactory scopeFactory, IDistributedLockProvider synchronizationProvider, ILogger<OutboxBackgroundService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _distributedLock = synchronizationProvider.CreateLock("OutboxBackgroundServiceLock");

        _scope = scopeFactory.CreateScope();
        _processor = _scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
    }

    public override void Dispose()
    {
        _scope.Dispose();

        base.Dispose();
    }

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
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    private async Task StartProcessingAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _processor.ProcessAsync(stoppingToken);
            await Task.Delay(1000, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not NoDbContextAssignedException)
        {
            // TODO:
            _logger.LogError(ex, "OutboxBackgroundService Error");
            await Task.Delay(3000, stoppingToken);
        }
    }
}
