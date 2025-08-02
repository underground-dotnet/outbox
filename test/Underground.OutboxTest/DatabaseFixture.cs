using Microsoft.Extensions.Logging;

using Testcontainers.PostgreSql;
using Testcontainers.Xunit;

using Xunit.Sdk;

namespace Underground.OutboxTest;

public sealed class DatabaseFixture(IMessageSink messageSink) : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink)
{
    public IMessageSink MessageSink { get; } = messageSink;
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(messageSink));

    protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
    {
        return builder.WithImage("postgres:17.2").WithLogger(_loggerFactory.CreateLogger<PostgreSqlContainer>());
    }

    // used only through direct access in the tests. We could also just get it from the service provider.
    public TestDbContext CreateDbContext()
    {
        return new TestDbContext(Container, _loggerFactory);
    }
}