using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal partial class ProcessExceptionFromHandler<TContext, TEntity>(
    ServiceConfiguration<TContext, TEntity> config,
    IServiceProvider serviceProvider,
    ILogger<ProcessExceptionFromHandler<TContext, TEntity>> logger
)
    where TContext : DbContext
    where TEntity : class, IMessage
{
    internal async Task ExecuteAsync(MessageHandlerException ex, TEntity message, TContext dbContext, CancellationToken cancellationToken = default)
    {
        var policy = SelectPolicyForException(ex);

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

    // Exactly one policy runs. A policy on the handler registration wins over any global policy, however
    // broad its exception type, so one AddHandler chain reads as a complete override. Within a level the
    // nearest matching exception type wins, like a catch block.
    private ExceptionPolicy<TEntity>? SelectPolicyForException(MessageHandlerException ex)
    {
        if (ex.InnerException is not { } thrown)
        {
            return null;
        }

        var handlerPolicies = config.Registrations
            .Where(r => r.HandlerType == ex.HandlerType && r.MessageType == ex.MessageType)
            .SelectMany(r => r.ExceptionPolicies)
            .ToList();

        return SelectNearestPolicy(handlerPolicies, thrown.GetType())
            ?? SelectNearestPolicy(config.GlobalPolicies.ExceptionPolicies, thrown.GetType());
    }

    private static ExceptionPolicy<TEntity>? SelectNearestPolicy(List<ExceptionPolicy<TEntity>> policies, Type thrownType)
    {
        for (var candidate = thrownType; candidate is not null; candidate = candidate.BaseType)
        {
            // the same exception type registered twice at one level keeps the first registration
            var match = policies.Find(p => p.ExceptionType == candidate);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
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
