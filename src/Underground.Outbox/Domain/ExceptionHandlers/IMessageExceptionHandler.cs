using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

/// <summary>
/// Defines an exception handler for processing messages.
/// Implement this interface to add custom error handling operations when a message handler throws an exception.
/// Do not forget to register the implementation in the dependency injection container.
/// </summary>
/// <typeparam name="TEntity">The message entity type that implements <see cref="IMessage"/>.</typeparam>
public interface IMessageExceptionHandler<in TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Handles an exception that occurred while processing a message.
    /// </summary>
    /// <param name="ex">The exception thrown from the message handler.</param>
    /// <param name="message">The message being processed when the exception occurred.</param>
    /// <param name="dbContext">The database context for performing data operations.</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task HandleAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken);
}
