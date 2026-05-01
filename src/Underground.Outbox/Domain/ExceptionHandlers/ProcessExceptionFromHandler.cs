using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal class ProcessExceptionFromHandler<TEntity>(
    ServiceConfiguration<TEntity> config,
    IServiceProvider serviceProvider,
    ILogger<ProcessExceptionFromHandler<TEntity>> logger
) where TEntity : class, IMessage
{
    internal async Task ExecuteAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var policies = config.Registrations
            .Where(r => r.HandlerType == ex.HandlerType && r.MessageType == ex.MessageType)
            .SelectMany(r => r.ExceptionPolicies)
            .Where(p => p.ExceptionType.IsInstanceOfType(ex.InnerException))
            .ToList();

        foreach (var policy in policies)
        {
#pragma warning disable CA1873 // Avoid potentially expensive logging
            logger.LogInformation(
                "Executing exception policy {PolicyType} for handler {HandlerType} and exception {ExceptionType} on message {MessageId}",
                policy.GetType().Name,
                ex.HandlerType.Name,
                ex.InnerException?.GetType().Name,
                message.Id
            );
#pragma warning restore CA1873 // Avoid potentially expensive logging

            var exceptionHandler = policy.GetExceptionHandler(serviceProvider);
            await exceptionHandler.HandleAsync(ex, message, dbContext, cancellationToken).ConfigureAwait(false);
        }
    }
}
