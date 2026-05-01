using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Configuration.ExceptionPolicies;

internal sealed record DiscardExceptionPolicy<TEntity>(Type ExceptionType) : ExceptionPolicy<TEntity>(ExceptionType) where TEntity : class, IMessage
{
    public override IMessageExceptionHandler<TEntity> GetExceptionHandler(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<DiscardMessageOnExceptionHandler<TEntity>>();
    }
}
