using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Counts its runs, so a test can tell whether a transient failure replayed the handler or only the
/// statement that failed.
/// </summary>
[OutboxHandler<InboxOutboxDbContext>]
public class RetryMessageHandler(InboxOutboxDbContext dbContext) : IOutboxMessageHandler<RetryMessage>
{
    /// <summary>How often the handler ran. Static because the handler is resolved per message.</summary>
    public static int Calls { get; private set; }

    /// <summary>Clears the statics between tests, since they are shared across the whole collection.</summary>
    public static void Reset() => Calls = 0;

    public async Task HandleAsync(RetryMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        Calls++;

        // an outbox handler holds no transaction, so this write is its own
        dbContext.Users.Add(new User { Name = $"outbox-{message.Id}" });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
