using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Testcontainers.PostgreSql;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;

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

        // override ConcurrentProcessor with SynchronousProcessor for easier testing of async code
        services.AddScoped<ConcurrentProcessor<OutboxMessage>, SynchronousProcessor<OutboxMessage>>();
        services.AddScoped<SynchronousProcessor<OutboxMessage>>();

        return services;
    }
}