using Microsoft.Extensions.Hosting;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain;

internal sealed class BackgroundService<TEntity>(
    ConcurrentProcessor<TEntity> processor,
    HandlerRegistry<TEntity> registry,
    ServiceConfiguration<TEntity> config
) : BackgroundService where TEntity : class, IMessage
{
    private readonly ConcurrentProcessor<TEntity> _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    private readonly HandlerRegistry<TEntity> _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly ServiceConfiguration<TEntity> _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// Checks the Handler Registry before any message is claimed. Resolving it has already thrown if two
    /// modules claim one message type; what is left is configuration naming a handler nobody discovered.
    /// Both are checked here rather than while services are being registered, so that the order of the
    /// generated registration calls does not matter.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in _config.Registrations)
        {
            if (!_registry.Contains(registration.HandlerType, registration.MessageType))
            {
                throw new UnknownHandlerException(registration.HandlerType, registration.MessageType);
            }
        }

        return base.StartAsync(cancellationToken);
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
