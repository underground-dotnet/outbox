using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.ExceptionHandlers;

/// <summary>
/// What an Exception Policy does to a message whose Processing Attempt failed. Register the implementation
/// in the DI container.
/// </summary>
/// <typeparam name="TEntity">The message entity type that implements <see cref="IMessage"/>.</typeparam>
public interface IMessageExceptionHandler<in TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Handles the failure of a Processing Attempt. It runs after the retry has been recorded.
    /// </summary>
    /// <param name="ex">The cause: the exception that actually went wrong, never a library wrapper.</param>
    /// <param name="message">The message being processed when the exception occurred.</param>
    /// <param name="dbContext">The database context for performing data operations.</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task HandleAsync(Exception ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken);
}
