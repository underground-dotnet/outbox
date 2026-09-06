using System.Data;

using Microsoft.EntityFrameworkCore;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class CustomSqlMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<CustomSqlMessage>
{
    public async Task HandleAsync(CustomSqlMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        // the table and its columns are mapped in lower case, so the identifiers have to be too: quoted
        // the other way round this statement fails on its own and the handler proves nothing
        await dbContext.Database.ExecuteSqlAsync(
            $"""INSERT INTO "users" ("id", "name") VALUES (100, 'CustomSqlUser')""",
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}
