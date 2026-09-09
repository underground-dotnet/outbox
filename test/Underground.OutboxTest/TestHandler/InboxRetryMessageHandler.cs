using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Writes one row and counts its runs. The row is what makes a replayed run observable: it is written in
/// the inbox's transaction, so a replay rolls the first one back and only one survives.
/// </summary>
[InboxHandler<InboxOutboxDbContext>]
public class InboxRetryMessageHandler(InboxOutboxDbContext dbContext) : IInboxMessageHandler<InboxRetryMessage>
{
    /// <summary>How often the handler ran. Static because the handler is resolved per message.</summary>
    public static int Calls { get; private set; }

    /// <summary>Clears the statics between tests, since they are shared across the whole collection.</summary>
    public static void Reset() => Calls = 0;

    public async Task HandleAsync(InboxRetryMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        Calls++;

        dbContext.Users.Add(new User { Name = $"inbox-{message.Id}" });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
