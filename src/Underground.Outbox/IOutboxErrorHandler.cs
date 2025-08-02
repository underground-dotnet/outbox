using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IOutboxErrorHandler
{
    /// <summary>
    /// Indicate wether the processing should stop on error or if it should continue with the next message.
    /// </summary>
    public bool ShouldStopProcessingOnError { get; }

    public Task HandleErrorAsync(DbContext dbContext, OutboxMessage message, Exception exception, OutboxServiceConfiguration config, CancellationToken cancellationToken);
}
