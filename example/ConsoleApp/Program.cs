using ConsoleApp;

using Microsoft.Extensions.Hosting;

using Underground.Outbox.Configuration;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.AddHandler<ExampleMessageHandler>();
});

IHost host = builder.Build();
await host.RunAsync();