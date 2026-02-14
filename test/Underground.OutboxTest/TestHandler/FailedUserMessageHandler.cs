using System.Data;

using Microsoft.EntityFrameworkCore.Storage;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class FailedUserMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<FailedUserMessage>
{
    public static IDbContextTransaction? CalledWithTransaction { get; set; } = null;

    public async Task HandleAsync(FailedUserMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithTransaction = dbContext.Database.CurrentTransaction;

        await dbContext.Users.AddAsync(new User { Name = "Testuser" }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}