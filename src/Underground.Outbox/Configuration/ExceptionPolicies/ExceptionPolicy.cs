using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Configuration.ExceptionPolicies;

public abstract record ExceptionPolicy<TEntity>(Type ExceptionType) where TEntity : class, IMessage
{
    public abstract IMessageExceptionHandler<TEntity> GetExceptionHandler(IServiceProvider serviceProvider);
}
