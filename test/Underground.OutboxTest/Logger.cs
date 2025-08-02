using Microsoft.Extensions.Logging;

using Xunit.Sdk;

namespace Underground.OutboxTest;

public static class LoggerExtensions
{
    public static ILoggingBuilder ConfigureTestLogger(this ILoggingBuilder builder, IMessageSink messageSink)
    {
        builder.AddXUnit(messageSink)
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Testcontainers.PostgreSql.PostgreSqlContainer", LogLevel.Error)
            .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);

        return builder;
    }
}