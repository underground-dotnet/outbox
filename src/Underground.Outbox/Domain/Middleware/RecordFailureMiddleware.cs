using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Turns a Handler that threw into a recorded attempt: pushes the message out of sight for its backoff
/// delay, then consults the exception policies. Reports the failure rather than rethrowing, so one bad
/// message costs its own Group and nothing else.
/// </summary>
/// <remarks>
/// The retry is written before the policies run because it is the guarded write, and a lost Lease has to
/// be discovered before consumer code - which writes by id and cannot be guarded - touches the message.
/// The exception goes onto the <see cref="ProcessingAttempt"/> rather than being logged here, so
/// <see cref="LogMessageMiddleware{TContext, TEntity}"/> reports it in its one outcome line.
/// </remarks>
internal sealed class RecordFailureMiddleware<TContext, TEntity>(
    TContext dbContext,
    ScheduleRetry<TContext, TEntity> scheduleRetry
) : IMessageMiddleware<TContext, TEntity> where TContext : DbContext
    where TEntity : class, IMessage
{
    public async Task<ProcessingAttempt> ExecuteAsync(TEntity message, IServiceScope scope, MessageMiddlewareDelegate next, CancellationToken cancellationToken)
    {
        Exception failure;

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex;
        }

        // clear tracked entities, so the exception handler works against a clean context
        dbContext.ChangeTracker.Clear();

        var stillOurs = await scheduleRetry.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);

        if (!stillOurs)
        {
            return ProcessingAttempt.LeaseLost(failure);
        }

        // only an exception the Handler itself raised has a policy to consult
        if (failure is MessageHandlerException handlerException)
        {
            // from the handling scope, so the exception handler sees the same services the Handler saw
            var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TContext, TEntity>>();

            await processHandlerException.ExecuteAsync(handlerException, message, dbContext, cancellationToken).ConfigureAwait(false);
        }

        return ProcessingAttempt.Failed(failure);
    }
}
