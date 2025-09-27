using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Testcontainers.PostgreSql;

namespace Underground.OutboxTest;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBaseServices(this IServiceCollection services, PostgreSqlContainer container, ITestOutputHelper outputHelper)
    {
        // setup logging so that the ILogger can be resolved from the service provider (Dependency Injection)
        services.AddLogging(builder => builder.ConfigureTestLogger(outputHelper));

        // setup DBContext to be available through Dependency Injection
        var loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(outputHelper));
        services.AddDbContext<TestDbContext>(options => TestDbContext.ConfigureDbContext(options, container, loggerFactory));

        return services;
    }
}