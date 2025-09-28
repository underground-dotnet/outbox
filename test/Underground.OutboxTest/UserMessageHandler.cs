using System.Data;

using Microsoft.EntityFrameworkCore.Storage;

using Underground.Outbox;

namespace Underground.OutboxTest;

public class UserMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<ExampleMessage>
{
    public static IDbContextTransaction? CalledWithTransaction { get; set; } = null;

    public async Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        CalledWithTransaction = dbContext.Database.CurrentTransaction;

        await dbContext.Users.AddAsync(new User { Name = "Testuser" }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}