using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Turns a Handler that threw into a recorded attempt: pushes the message out of sight for its backoff
/// delay, then consults the exception policies. Reports the failure to the caller rather than
/// rethrowing, so that one bad message costs its own Group and nothing else.
/// </summary>
/// <remarks>
/// The retry is written before the policies run rather than after, because it is the guarded write: it
/// is what establishes that this worker still holds the message. An exception policy writes by id and
/// cannot be guarded - it is consumer code - so a lost Lease has to be discovered before one is allowed
/// to delete or complete a message some other worker now owns.
/// <para>
/// The exception is put on the <see cref="Attempt"/> rather than logged here, so that
/// <see cref="LogMessageStage{TEntity}"/> reports it as part of the one outcome line it writes per message.
/// </para>
/// </remarks>
internal sealed class RecordFailureStage<TEntity>(
    IDbContext dbContext,
    ScheduleRetry<TEntity> scheduleRetry
) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    public async Task<Attempt> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
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

        // the try block returns on success, so reaching here means the message failed

        // clear all tracked entities, because processing failed. The exception handler can then use the
        // clean context to perform db operations.
        dbContext.ChangeTracker.Clear();

        // records the attempt and moves the message out of sight for the backoff delay, so the next
        // run does not retry it immediately
        var stillOurs = await scheduleRetry.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);

        if (!stillOurs)
        {
            return Attempt.LeaseLost(failure);
        }

        // only an exception the Handler itself raised has a policy to consult; anything else has nothing
        // to match against and falls through with the retry already recorded
        if (failure is MessageHandlerException handlerException)
        {
            // resolved from the scope the message is handled in rather than from this stage's own, so that
            // an exception handler sees the same services the Handler that raised it saw
            var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TEntity>>();

            await processHandlerException.ExecuteAsync(handlerException, message, dbContext, cancellationToken).ConfigureAwait(false);
        }

        return Attempt.Failed(failure);
    }
}
