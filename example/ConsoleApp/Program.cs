﻿using ConsoleApp;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Testcontainers.PostgreSql;

using Underground.Outbox;
using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

#pragma warning disable CA1305 // Specify IFormatProvider

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

var postgreSqlContainer = new PostgreSqlBuilder().WithImage("postgres:17.2").Build();
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
var dbContext = host.Services.GetRequiredService<AppDbContext>();
await dbContext.Database.EnsureCreatedAsync();

await using (var transaction = await dbContext.Database.BeginTransactionAsync())
{
    for (int i = 0; i < 10; i++)
    {
        var partition = (i % 3).ToString();
        var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage($"partition {partition}: {i}"), partition);
        await outbox.AddMessageAsync(dbContext, message, CancellationToken.None);
    }

    await transaction.CommitAsync();
}

await host.RunAsync();

#pragma warning restore CA1305 // Specify IFormatProvider
