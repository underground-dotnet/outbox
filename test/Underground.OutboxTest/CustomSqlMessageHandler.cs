using System.Data;

using Microsoft.EntityFrameworkCore;

using Underground.Outbox;

namespace Underground.OutboxTest;

public class CustomSqlMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<ExampleMessage>
{
    public async Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Users" ("Id", "Name") VALUES (100, 'CustomSqlUser')""",
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}
