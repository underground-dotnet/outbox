using Microsoft.Extensions.Logging;

using Testcontainers.PostgreSql;
using Testcontainers.Xunit;

[assembly: CaptureConsole]

namespace Underground.OutboxTest;

public partial class DatabaseTest(ITestOutputHelper testOutputHelper) : ContainerTest<PostgreSqlBuilder, PostgreSqlContainer>(testOutputHelper)
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(testOutputHelper));

    protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
    {
        return builder.WithImage("postgres:17.2").WithLogger(_loggerFactory.CreateLogger<PostgreSqlContainer>());
    }

    // used only through direct access in the tests. We could also just get it from the service provider.
    public TestDbContext CreateDbContext()
    {
        var dbContext = new TestDbContext(Container, _loggerFactory);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}