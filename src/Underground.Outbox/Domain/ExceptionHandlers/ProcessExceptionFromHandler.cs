using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Configuration.ExceptionPolicies;
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
        var policies = GetPoliciesForException(ex);

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

    private List<ExceptionPolicy<TEntity>> GetPoliciesForException(MessageHandlerException ex)
    {
        // make sure every policy is executed only once, even if it matches both handler-specific and global policies
        var dict = new Dictionary<Type, ExceptionPolicy<TEntity>>();

        var policies = config.Registrations
            // Filter filter policies for the handler and message type of the exception
            .Where(r => r.HandlerType == ex.HandlerType && r.MessageType == ex.MessageType)
            .SelectMany(r => r.ExceptionPolicies)
            .Where(p => p.ExceptionType.IsInstanceOfType(ex.InnerException))
            .ToList();

        policies.AddRange(config.GlobalPolicies.ExceptionPolicies
            .Where(p => p.ExceptionType.IsInstanceOfType(ex.InnerException)));

        foreach (var policy in policies)
        {
            // Get the generic base type which ignores the specific exception type, so that we can avoid adding the same policy twice.
            var baseType = policy.GetType().BaseType?.GetGenericTypeDefinition();
            if (baseType is null)
            {
                continue;
            }

            dict.TryAdd(baseType, policy);
        }

        return dict.Values.ToList();
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Executing exception policy {PolicyType} for handler {HandlerType} and exception {ExceptionType} on message {MessageId}")]
    private partial void LogExecutingExceptionPolicy(string policyType, string handlerType, string? exceptionType, long messageId);
}
