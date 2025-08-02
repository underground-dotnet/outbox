using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Testcontainers.PostgreSql;

using Xunit.Sdk;

namespace Underground.OutboxTest;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBaseServices(this IServiceCollection services, PostgreSqlContainer container, IMessageSink messageSink)
    {
        // setup logging so that the ILogger can be resolved from the service provider (Dependency Injection)
        services.AddLogging(builder => builder.ConfigureTestLogger(messageSink));

        // setup DBContext to be available through Dependency Injection
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(messageSink));
        services.AddDbContext<TestDbContext>(options => TestDbContext.ConfigureDbContext(options, container, loggerFactory));

        return services;
    }
}