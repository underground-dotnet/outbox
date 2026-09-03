using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using System.Runtime.CompilerServices;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Records a failed attempt and pushes the message out of sight for the backoff delay, so that a
/// handler that keeps failing is not retried in a hot loop.
/// </summary>
internal sealed class ScheduleRetry<TEntity>(IDbContext dbContext, ServiceConfiguration<TEntity> config) where TEntity : class, IMessage
{
#pragma warning disable S2743 // A static field in a generic type is not shared among instances of different close constructed types.
    private static readonly ConditionalWeakTable<IModel, string> SqlByModel = [];
#pragma warning restore S2743 // A static field in a generic type is not shared among instances of different close constructed types.

    private readonly RetryBackoff _backoff = new(config.BackoffBase, config.MaxBackoff, config.BackoffJitter);

    internal async Task ExecuteAsync(TEntity message, CancellationToken cancellationToken)
    {
        var sql = SqlByModel.GetValue(dbContext.Model, static model => BuildSql(model));

        await dbContext.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("id", message.Id),
                new NpgsqlParameter("delay", _backoff.DelayFor(message.RetryCount)),
            ],
            cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSql(IModel model)
    {
        var table = MessageTable.For<TEntity>(model);

        // The application supplies an interval and never an instant, so the new visibility is anchored to the
        // database's clock: an instance whose own clock is skewed cannot bring a message back early or late.
        return $"""
            UPDATE {table.Name}
            SET {table.RetryCount} = {table.RetryCount} + 1,
                {table.VisibleAt} = clock_timestamp() + @delay
            WHERE {table.Id} = @id
            """;
    }
}
