using ConsoleApp;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Testcontainers.PostgreSql;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

#pragma warning disable CA1305 // Specify IFormatProvider

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

var postgreSqlContainer = new PostgreSqlBuilder("postgres:18.1").Build();
await postgreSqlContainer.StartAsync();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options
        .UseNpgsql(postgreSqlContainer.GetConnectionString())
        .AddInterceptors(sp.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor<AppDbContext>>());
});

builder.Services.AddAppDbContextOutboxServices(cfg =>
{
    cfg.Schema = "public";
    cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();
    cfg.AddHandler<ExampleMessageHandler, SecondMessage>()
        .OnException<InvalidOperationException>().Discard()
        .OnException<TimeoutException>().Discard();
});
builder.Services.AddAppDbContextInboxServices(cfg =>
{
    cfg.Schema = "public";
    cfg.Policies.OnException<FileNotFoundException>().Discard();

    cfg.AddHandler<InboxMessageHandler, ExampleMessage>();
});

IHost host = builder.Build();

// IOutbox, IInbox and the DbContext are scoped, so seeding needs its own scope rather than the root provider
await using (var scope = host.Services.CreateAsyncScope())
{
    var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<AppDbContext>>();
    var inbox = scope.ServiceProvider.GetRequiredService<IInbox<AppDbContext>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    await using (var transaction = await dbContext.Database.BeginTransactionAsync())
    {
        for (int i = 0; i < 10; i++)
        {
            var groupKey = (i % 3).ToString();
            var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage($"group {groupKey}: {i}"), groupKey);
            await outbox.AddMessageAsync(dbContext, message, CancellationToken.None);

            var inboxMessage = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage($"inbox message: {i}"));
            await inbox.AddMessageAsync(dbContext, inboxMessage, CancellationToken.None);
        }

        var secondMessage = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new SecondMessage("Test Message"));
        await outbox.AddMessageAsync(dbContext, secondMessage, CancellationToken.None);

        await transaction.CommitAsync();
    }

    // qualified by the same schema the registration named, which is where the library's own statements
    // look for it
    var count = await dbContext.Database.SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox").SingleAsync();
    Console.WriteLine($"Added {count} messages to outbox.");
}

await host.RunAsync();

#pragma warning restore CA1305 // Specify IFormatProvider
