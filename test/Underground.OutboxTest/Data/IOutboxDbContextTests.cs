using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.OutboxTest.Data;

public class IOutboxDbContextTests(ITestOutputHelper testOutputHelper) : DatabaseTest(testOutputHelper)
{
    [Fact]
    public async Task DynamicTableName()
    {
        var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        context.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(10)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
        var result = await context.OutboxMessages.FromSqlRaw($"SELECT * FROM {context.GetTableName<OutboxMessage>()}").ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.
        Assert.Single(result);
    }
}
