using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using PerformanceApp;

using Testcontainers.PostgreSql;

using Underground.Outbox;
using Underground.Outbox.Configuration;

#pragma warning disable CA1305 // Specify IFormatProvider

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

var postgreSqlContainer = new PostgreSqlBuilder().WithImage("postgres:18.1").Build();
await postgreSqlContainer.StartAsync();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(postgreSqlContainer.GetConnectionString());
});

builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.BatchSize = 500;
    cfg.AddHandler<ExampleMessageHandler>();
});

IHost host = builder.Build();

var outbox = host.Services.GetRequiredService<IOutbox>();
var dbContext = host.Services.GetRequiredService<AppDbContext>();
await dbContext.Database.EnsureCreatedAsync();

// seed data
await using (var dataSource = NpgsqlDataSource.Create(postgreSqlContainer.GetConnectionString()))
{
    using var connection = await dataSource.OpenConnectionAsync();
    await using var writer = await connection.BeginBinaryImportAsync("COPY public.outbox (event_id, created_at, type, partition_key, data, retry_count) FROM STDIN (FORMAT BINARY)");

    var msgType = typeof(ExampleMessage).AssemblyQualifiedName!;

    for (int i = 0; i < 5_000; i++)
    {
        // var partition = (i % 3).ToString();
        await writer.StartRowAsync();
        await writer.WriteAsync(Guid.NewGuid());
        await writer.WriteAsync(DateTime.UtcNow, NpgsqlTypes.NpgsqlDbType.TimestampTz);
        await writer.WriteAsync(msgType);
        await writer.WriteAsync("A");
        var message = new ExampleMessage($"message: {i}");
        await writer.WriteAsync(JsonSerializer.Serialize(message));
        await writer.WriteAsync(0);
    }

    await writer.CompleteAsync();
}

outbox.ProcessMessages();

await host.RunAsync();

#pragma warning restore CA1305 // Specify IFormatProvider
