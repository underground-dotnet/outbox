using Microsoft.EntityFrameworkCore;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.OutboxTest;

public class MockErrorHandler : IOutboxErrorHandler
{
    public static OutboxMessage? CalledWithMessage { get; set; }
    public static Exception? CalledWithException { get; set; }
    public static bool StopProcessing { get; set; } = false;
    public bool ShouldStopProcessingOnError => StopProcessing;

    public Task HandleErrorAsync(DbContext dbContext, OutboxMessage message, Exception exception, OutboxServiceConfiguration config, CancellationToken cancellationToken)
    {
        CalledWithMessage = message;
        CalledWithException = exception;
        return Task.CompletedTask;
    }
}
