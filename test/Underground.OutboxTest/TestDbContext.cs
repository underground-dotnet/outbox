using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

    public TestDbContext(TestDatabase database, ILoggerFactory loggerFactory, ProcessMessagesOnSaveChangesInterceptor<TestDbContext>? interceptor) : base(
        ConfigureDbContext(new DbContextOptionsBuilder<TestDbContext>(), database, loggerFactory, interceptor).Options
    )
    {
    }

    public static DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder options,
        TestDatabase database,
        ILoggerFactory loggerFactory,
        ProcessMessagesOnSaveChangesInterceptor<TestDbContext>? interceptor
    )
    {
        var builder = options
            .UseNpgsql(database.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .EnableSensitiveDataLogging();

        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder;
    }
}
