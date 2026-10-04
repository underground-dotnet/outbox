using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

/// <summary>
/// Selects the one Exception Policy that matches the cause of a failed Processing Attempt, and runs it.
/// </summary>
internal sealed partial class ApplyExceptionPolicy<TEntity>(
    ServiceConfiguration<TEntity> config,
    HandlerRegistry<TEntity> registry,
    IServiceProvider serviceProvider,
    ILogger<ApplyExceptionPolicy<TEntity>> logger
) where TEntity : class, IMessage
{
    internal async Task ExecuteAsync(Exception cause, TEntity message, IDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var policy = SelectPolicy(cause, message);

        if (policy is null)
        {
            LogNoExceptionPolicyMatched(cause.GetType().Name, message.Id);
            return;
        }

        LogExecutingExceptionPolicy(policy.GetType().Name, cause.GetType().Name, message.Id);

        var exceptionHandler = policy.GetExceptionHandler(serviceProvider);
        await exceptionHandler.HandleAsync(cause, message, dbContext, cancellationToken).ConfigureAwait(false);
    }

    // Exactly one policy runs. A policy on the message's Handler wins over any global policy, however broad
    // its exception type, so one ForHandler chain reads as a complete override. Within a level the nearest
    // matching exception type wins, like a catch block. An unknown type has no Handler, so only global
    // policies can match it.
    private ExceptionPolicy<TEntity>? SelectPolicy(Exception cause, TEntity message)
    {
        var handlerPolicies = registry.TryGetEntry(message.Type, out var entry)
            ? config.Registrations
                .Where(r => r.HandlerType == entry.HandlerType && r.MessageType == entry.MessageType)
                .SelectMany(r => r.ExceptionPolicies)
                .ToList()
            : [];

        return SelectNearestPolicy(handlerPolicies, cause)
            ?? SelectNearestPolicy(config.GlobalPolicies.ExceptionPolicies, cause);
    }

    private static ExceptionPolicy<TEntity>? SelectNearestPolicy(List<ExceptionPolicy<TEntity>> policies, Exception cause)
    {
        // a Deployment Gap matches only a policy that names one, so the walk stops at DeploymentGapException
        // and never reaches Exception (ADR 0012)
        var stopAfter = cause is DeploymentGapException ? typeof(DeploymentGapException) : typeof(object);

        for (var candidate = cause.GetType(); candidate is not null; candidate = candidate.BaseType)
        {
            // the same exception type registered twice at one level keeps the first registration
            var match = policies.Find(p => p.ExceptionType == candidate);
            if (match is not null || candidate == stopAfter)
            {
                return match;
            }
        }

        return null;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Executing exception policy {PolicyType} for exception {ExceptionType} on message {MessageId}")]
    private partial void LogExecutingExceptionPolicy(string policyType, string exceptionType, long messageId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "No exception policy matched exception {ExceptionType} on message {MessageId}")]
    private partial void LogNoExceptionPolicyMatched(string exceptionType, long messageId);
}
