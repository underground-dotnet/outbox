using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Testcontainers.PostgreSql;

namespace Underground.OutboxTest;

public class TestDbContext : DbContext
{
    public DbSet<User> Users { get; set; }

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public TestDbContext(PostgreSqlContainer container, ILoggerFactory loggerFactory) : base(
        ConfigureDbContext(new DbContextOptionsBuilder<TestDbContext>(), container, loggerFactory).Options
    )
    {
    }

    public static DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder options,
        PostgreSqlContainer container,
        ILoggerFactory loggerFactory)
    {
        var builder = options
            .UseNpgsql(container.GetConnectionString())
            .UseLoggerFactory(loggerFactory)
            .EnableSensitiveDataLogging();

        return builder;
    }
}
