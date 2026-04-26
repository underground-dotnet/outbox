using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Configuration.ExceptionPolicies;

public abstract record ExceptionPolicy<TEntity, THandler>(Type ExceptionType)
    where TEntity : class, IMessage
    where THandler : IMessageExceptionHandler<TEntity>
{
    // public abstract Task HandleExceptionAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken);
}


// public abstract record ExPolicy<TEntity, THandler>(Type ExceptionType)
//     where TEntity : class, IMessage
//     where THandler : IMessageExceptionHandler<TEntity>;