using ConsoleApp;

using Microsoft.Extensions.Hosting;

using Underground;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
// builder.Services.AddHostedService<Worker>();
builder.Services.AddInboxServices("example", cfg =>
{
    cfg.AddHandler<ExampleMessageHandler>();
});

IHost host = builder.Build();
await host.RunAsync();