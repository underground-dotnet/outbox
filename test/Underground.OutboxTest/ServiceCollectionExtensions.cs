using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;

namespace Underground.OutboxTest;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBaseServices(this IServiceCollection services, TestDatabase database, ITestOutputHelper outputHelper)
    {
        // setup logging so that the ILogger can be resolved from the service provider (Dependency Injection)
        services.AddLogging(builder => builder.ConfigureTestLogger(outputHelper));

        // setup DBContext to be available through Dependency Injection
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(outputHelper));
        services.AddDbContext<TestDbContext>(options => TestDbContext.ConfigureDbContext(options, database, loggerFactory, interceptor: null));

        // every handler in this assembly is discovered, so each test sees all of them; their message
        // types are distinct, so a test only ever receives the messages it wrote
        services.AddUndergroundOutboxTestMessageHandlers();

        return services;
    }
}