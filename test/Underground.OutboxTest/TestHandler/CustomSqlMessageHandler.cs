using System.Data;

using Microsoft.EntityFrameworkCore;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class CustomSqlMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<CustomSqlMessage>
{
    public async Task HandleAsync(CustomSqlMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Users" ("Id", "Name") VALUES (100, 'CustomSqlUser')""",
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}
