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
        // var policies = new List<ExceptionPolicy<TEntity>>();
        // var seenPolicies = new HashSet<(Type PolicyType, Type ExceptionType)>();

        // void AddPolicies(IEnumerable<ExceptionPolicy<TEntity>> candidatePolicies)
        // {
        //     foreach (var policy in candidatePolicies.Where(p => p.ExceptionType.IsInstanceOfType(ex.InnerException)))
        //     {
        //         var key = (policy.GetType(), policy.ExceptionType);
        //         if (seenPolicies.Add(key))
        //         {
        //             policies.Add(policy);
        //         }
        //     }
        // }

        // AddPolicies(config.Registrations
        //     .Where(r => r.HandlerType == ex.HandlerType && r.MessageType == ex.MessageType)
        //     .SelectMany(r => r.ExceptionPolicies));
        // AddPolicies(config.Policies.ExceptionPolicies);

        // parentEx und childEx, sollen beide greifen?
        var dict = new Dictionary<Type, ExceptionPolicy<TEntity>>();

        var policies = config.Registrations
            .Where(r => r.HandlerType == ex.HandlerType && r.MessageType == ex.MessageType)
            .SelectMany(r => r.ExceptionPolicies)
            .Where(p => p.ExceptionType.IsInstanceOfType(ex.InnerException))
            .ToList();

        config.GlobalPolicies.ExceptionPolicies
            .Where(p => p.ExceptionType.IsInstanceOfType(ex.InnerException))
            .ToList()
            .ForEach(p =>
            {
                if (!policies.Any(existingPolicy => existingPolicy.GetType() == p.GetType() && existingPolicy.ExceptionType == p.ExceptionType))
                {
                    policies.Add(p);
                }
            });

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
