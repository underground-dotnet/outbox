using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MultiProjectApp;

using MultiProjectLib;

using Testcontainers.PostgreSql;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;

#pragma warning disable CA1305 // Specify IFormatProvider

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

var postgreSqlContainer = new PostgreSqlBuilder("postgres:18.1").Build();
await postgreSqlContainer.StartAsync();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(postgreSqlContainer.GetConnectionString());
});

builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.AddHandler<ExampleMessageHandler>();
});

IHost host = builder.Build();

var outbox = host.Services.GetRequiredService<IOutbox>();
var inbox = host.Services.GetRequiredService<IInbox>();
var dbContext = host.Services.GetRequiredService<AppDbContext>();
await dbContext.Database.EnsureCreatedAsync();

await using (var transaction = await dbContext.Database.BeginTransactionAsync())
{
    for (int i = 0; i < 10; i++)
    {
        var partition = (i % 3).ToString();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage($"partition {partition}: {i}"), partition);
        await outbox.AddMessageAsync(dbContext, message, CancellationToken.None);

        var inboxMessage = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage($"inbox message: {i}"));
        await inbox.AddMessageAsync(dbContext, inboxMessage, CancellationToken.None);
    }

    await transaction.CommitAsync();
}

// using custom table name "outbox_msgs"
var count = await dbContext.Database.SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox_msgs").SingleAsync();
Console.WriteLine($"Added {count} messages to outbox.");

await host.RunAsync();

#pragma warning restore CA1305 // Specify IFormatProvider
