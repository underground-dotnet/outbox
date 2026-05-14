using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.OutboxTest.TestPolicies;

public record MarkAsProcessedExceptionPolicy<TEntity>(Type ExceptionType) : ExceptionPolicy<TEntity>(ExceptionType) where TEntity : class, IMessage
{
    public override IMessageExceptionHandler<TEntity> GetExceptionHandler(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<MarkAsProcessedExceptionHandler<TEntity>>();
    }
}
