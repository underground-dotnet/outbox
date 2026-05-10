using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Testcontainers.PostgreSql;

using Underground.Outbox;
using Underground.Outbox.Data;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest;

public class TestDbContext : DbContext, IOutboxDbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public TestDbContext(PostgreSqlContainer container, ILoggerFactory loggerFactory, ProcessMessagesOnSaveChangesInterceptor? interceptor) : base(
        ConfigureDbContext(new DbContextOptionsBuilder<TestDbContext>(), container, loggerFactory, interceptor).Options
    )
    {
    }

    public static DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder options,
        PostgreSqlContainer container,
        ILoggerFactory loggerFactory,
        ProcessMessagesOnSaveChangesInterceptor? interceptor
    )
    {
        var builder = options
            .UseNpgsql(container.GetConnectionString())
            .UseLoggerFactory(loggerFactory)
            .EnableSensitiveDataLogging();

        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder;
    }
}
