using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal partial class ProcessExceptionFromHandler<TEntity>(
    ExceptionPolicyTable<TEntity> policies,
    IServiceProvider serviceProvider,
    ILogger<ProcessExceptionFromHandler<TEntity>> logger
) where TEntity : class, IMessage
{
    internal async Task ExecuteAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var policy = ex.InnerException is { } thrown
            ? policies.Select(ex.HandlerType, ex.MessageType, thrown)
            : null;

        if (policy is null)
        {
            LogNoExceptionPolicyMatched(
                ex.HandlerType.Name,
                ex.InnerException?.GetType().Name,
                message.Id);
            return;
        }

        LogExecutingExceptionPolicy(
            policy.GetType().Name,
            ex.HandlerType.Name,
            ex.InnerException?.GetType().Name,
            message.Id);

        var exceptionHandler = policy.GetExceptionHandler(serviceProvider);
        await exceptionHandler.HandleAsync(ex, message, dbContext, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Executing exception policy {PolicyType} for handler {HandlerType} and exception {ExceptionType} on message {MessageId}")]
    private partial void LogExecutingExceptionPolicy(string policyType, string handlerType, string? exceptionType, long messageId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "No exception policy matched handler {HandlerType} and exception {ExceptionType} on message {MessageId}")]
    private partial void LogNoExceptionPolicyMatched(string handlerType, string? exceptionType, long messageId);
}
