using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal partial class ProcessExceptionFromHandler<TEntity>(
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
            LogExecutingExceptionPolicy(
                policy.GetType().Name,
                ex.HandlerType.Name,
                ex.InnerException?.GetType().Name,
                message.Id);

            var exceptionHandler = policy.GetExceptionHandler(serviceProvider);
            await exceptionHandler.HandleAsync(ex, message, dbContext, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Executing exception policy {PolicyType} for handler {HandlerType} and exception {ExceptionType} on message {MessageId}")]
    private partial void LogExecutingExceptionPolicy(string policyType, string handlerType, string? exceptionType, long messageId);
}
