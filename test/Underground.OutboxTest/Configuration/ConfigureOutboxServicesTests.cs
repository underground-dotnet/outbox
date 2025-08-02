using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Exceptions;

namespace Underground.OutboxTest.Configuration;

public class ConfigureOutboxServicesTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ConfigureOutboxServicesTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ThrowsExceptionWhenNoDbContextIsSet()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(_fixture.Container, _fixture.MessageSink);

        // Act & Assert
        Assert.Throws<NoDbContextAssignedException>(() =>
            // This will throw because no DbContext is assigned
            serviceCollection.AddOutboxServices(cfg => { })
        );
    }

    // TODO: should be cannot, multiple handlers is not supported
    [Fact]
    public void CanAddMultipleHandlersForSameMessageType()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(_fixture.Container, _fixture.MessageSink);

        serviceCollection.AddOutboxServices(cfg =>
        {
            cfg.UseDbContext<TestDbContext>();
            cfg.AddHandler<ExampleMessageHandler>();
            cfg.AddHandler<ExampleMessageAnotherHandler>(); // Adding a second handler for same message type
        });

        // Act
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var handlers = serviceProvider.GetServices<IOutboxMessageHandler<ExampleMessage>>();

        // Assert
        Assert.Equal(2, handlers.Count());
    }

    [Fact]
    public async Task CreateDbTable()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBaseServices(_fixture.Container, _fixture.MessageSink);

        // Act
        serviceCollection.AddOutboxServices(cfg => cfg.UseDbContext<TestDbContext>());

        // Assert
        var context = _fixture.CreateDbContext();
        Assert.True(await TableExists(context, "outbox"));

        var tableCount = await context.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_type = 'BASE TABLE'
        """).SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, tableCount);
    }

    private static async Task<bool> TableExists(DbContext context, string tableName)
    {
        return await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT
                FROM pg_tables
                WHERE schemaname = 'public'
                AND tablename = {tableName}
            ) AS "Value"
        """).SingleAsync();
    }
}
