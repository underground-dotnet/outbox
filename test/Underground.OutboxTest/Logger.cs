using Microsoft.Extensions.Logging;

namespace Underground.OutboxTest;

public static class LoggerExtensions
{
    public static ILoggingBuilder ConfigureTestLogger(this ILoggingBuilder builder, ITestOutputHelper outputHelper)
    {
        builder.AddXUnit(outputHelper)
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Testcontainers.PostgreSql.PostgreSqlContainer", LogLevel.Error)
            .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);

        return builder;
    }
}