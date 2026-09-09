using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox;
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

        return services;
    }

    /// <summary>
    /// Registers the <see cref="TestDbContext"/> outbox in the schema the template database put its tables
    /// in. Here rather than in every test, so that the schema is written once.
    /// </summary>
    internal static IServiceCollection AddTestOutbox(this IServiceCollection services, Action<OutboxServiceConfiguration<TestDbContext>>? configure = null)
        => services.AddTestDbContextOutboxServices(cfg =>
        {
            cfg.Schema = TestSchemas.Default;
            configure?.Invoke(cfg);
        });

    /// <summary>Registers the <see cref="InboxOutboxDbContext"/> outbox.</summary>
    internal static IServiceCollection AddInboxOutboxOutbox(this IServiceCollection services, Action<OutboxServiceConfiguration<InboxOutboxDbContext>>? configure = null)
        => services.AddInboxOutboxDbContextOutboxServices(cfg =>
        {
            cfg.Schema = TestSchemas.Default;
            configure?.Invoke(cfg);
        });

    /// <summary>Registers the <see cref="InboxOutboxDbContext"/> inbox.</summary>
    internal static IServiceCollection AddInboxOutboxInbox(this IServiceCollection services, Action<InboxServiceConfiguration<InboxOutboxDbContext>>? configure = null)
        => services.AddInboxOutboxDbContextInboxServices(cfg =>
        {
            cfg.Schema = TestSchemas.Default;
            configure?.Invoke(cfg);
        });

    /// <summary>Registers one module's context and its outbox, in that module's own schema.</summary>
    internal static IServiceCollection AddModuleA(this IServiceCollection services, TestDatabase database, ILoggerFactory loggerFactory)
    {
        services.AddDbContext<ModuleADbContext>((sp, options) => options
            .UseNpgsql(database.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .AddInterceptors(sp.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor<ModuleADbContext>>()));
        services.AddModuleADbContextOutboxServices(cfg =>
        {
            cfg.Schema = ModuleADbContext.Schema;
            cfg.AddHandler<TestHandler.ModuleAHandler, TestHandler.SharedContract>();
        });

        return services;
    }

    /// <summary>Registers the neighbouring module's context and its outbox, in a schema of its own.</summary>
    internal static IServiceCollection AddModuleB(this IServiceCollection services, TestDatabase database, ILoggerFactory loggerFactory)
    {
        services.AddDbContext<ModuleBDbContext>((sp, options) => options
            .UseNpgsql(database.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .AddInterceptors(sp.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor<ModuleBDbContext>>()));
        services.AddModuleBDbContextOutboxServices(cfg =>
        {
            cfg.Schema = ModuleBDbContext.Schema;
            cfg.AddHandler<TestHandler.ModuleBHandler, TestHandler.SharedContract>();
        });

        return services;
    }
}
