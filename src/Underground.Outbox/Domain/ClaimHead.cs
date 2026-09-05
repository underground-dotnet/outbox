using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using System.Runtime.CompilerServices;

using Underground.Outbox.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Underground.Outbox.Domain;

internal abstract partial class ClaimHead<TEntity>(IDbContext dbContext, ILogger<ClaimHead<TEntity>> logger) where TEntity : class, IMessage
{
#pragma warning disable S2743 // A static field in a generic type is not shared among instances of different close constructed types.
    private static readonly ConditionalWeakTable<IModel, string> SqlByModel = [];
#pragma warning restore S2743 // A static field in a generic type is not shared among instances of different close constructed types.

    /// <summary>
    /// Claims the Head - the oldest settled unhandled message - of whichever Group currently offers the
    /// oldest one. Returns <c>null</c> when no Group offers anything: because there is nothing unhandled,
    /// because every candidate Head is not yet visible, and because another worker already holds the ones
    /// that are.
    /// </summary>
    /// <remarks>
    /// What holding the claim means is the subclass's answer: the inbox keeps the row lock for its
    /// transaction, the outbox commits a Lease and lets the lock go.
    /// </remarks>
    internal async Task<TEntity?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var sql = SqlByModel.GetValue(dbContext.Model, model => BuildSql(MessageTable.For<TEntity>(model)));

        var connection = dbContext.Database.GetDbConnection();
        var needsOpen = connection.State != ConnectionState.Open;

        if (needsOpen)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = sql;
                AddParameters(cmd);

                LogClaimSql(sql);
                var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        ? BuildEntityFromReader(reader)
                        : null;
                }
            }
        }
        finally
        {
            if (needsOpen)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The statement this side claims with, built around <see cref="LockedHeadCte"/>. The two sides differ
    /// only in what they do with the row that CTE locks.
    /// </summary>
    /// <param name="table">
    /// The table's identifiers, read off the EF model rather than written literally, since a consumer can
    /// remap any of them through EF Core mappings.
    /// </param>
    protected abstract string BuildSql(MessageTable table);

    /// <summary>
    /// Binds whatever <see cref="BuildSql"/> parameterised. Head discovery itself takes no parameters, so
    /// this does nothing unless a side added one.
    /// </summary>
    protected virtual void AddParameters(DbCommand command)
    {
        // nothing to bind by default
    }

    /// <summary>
    /// The Head discovery both sides share, as two CTEs named <c>heads</c> and <c>claimed</c>. The second
    /// yields at most one id, already locked for the calling transaction; a caller appends the statement
    /// that acts on it.
    /// </summary>
    protected static string LockedHeadCte(MessageTable table)
    {
        // Head discovery is deliberately in two stages. A Group's Head is the lowest (transaction_id, id)
        // among its settled unhandled rows *regardless of visibility*, and only then is visibility tested
        // against that one row. Filtering by visible_at first would hand out the message behind a Head that
        // is in backoff or scheduled for later, which is exactly the reordering this design exists to
        // prevent. A Group whose Head is invisible therefore offers nothing at all.
        //
        // The first CTE collects one Head per Group and the second takes the oldest of them that is both
        // visible and unlocked. That single statement is what distributes Groups across workers: there is no
        // discovery stage handing Groups out, every worker just runs this and the database decides.
        //
        // Ordering is by (transaction_id, id) rather than by id alone: identity values are handed out when a
        // row is inserted, not when its transaction commits, so a transaction that starts later but commits
        // first would otherwise have its message handled first.
        //
        // The settled filter withholds a message until no still-running transaction could yet insert an
        // earlier one into its Group. It is safe to apply it during Head discovery only because the sort key
        // is (transaction_id, id): an unsettled row always sorts after every settled one, so excluding it can
        // never promote a later message ahead of it. With id alone this would be a bug.
        //
        // FOR UPDATE cannot be applied alongside DISTINCT ON, so the Head set is a CTE and the locking
        // clause names the outer join back to the table. It is SKIP LOCKED rather than NOWAIT: a Head
        // another worker already holds is simply passed over in favour of the next Group's, instead of
        // aborting a statement that now spans every Group.
        //
        // The second CTE repeats processed_at IS NULL because the first reads its snapshot without a lock:
        // if a concurrent worker commits processed_at in between, FOR UPDATE re-evaluates only the repeated
        // predicate against the new row version, and without it that race hands out a handled message.
        //
        // Both instants are compared against clock_timestamp() rather than now(), which is frozen for the
        // transaction that the inbox holds open across its handler.
        return $"""
            WITH heads AS (
                SELECT DISTINCT ON ({table.GroupKey}) {table.Id}
                FROM {table.Name}
                WHERE {table.ProcessedAt} IS NULL
                AND {table.TransactionId} < pg_snapshot_xmin(pg_current_snapshot())
                ORDER BY {table.GroupKey}, {table.TransactionId}, {table.Id}
            ),
            claimed AS (
                SELECT m.{table.Id}
                FROM heads h
                JOIN {table.Name} m ON m.{table.Id} = h.{table.Id}
                WHERE m.{table.ProcessedAt} IS NULL
                AND m.{table.VisibleAt} <= clock_timestamp()
                ORDER BY m.{table.TransactionId}, m.{table.Id}
                LIMIT 1
                FOR UPDATE OF m SKIP LOCKED
            )
            """;
    }

    /// <summary>
    /// The projection <see cref="BuildEntityFromReader"/> expects, in order, over the table alias
    /// <see cref="LockedHeadCte"/> uses. Shared so that a side which returns its row through
    /// <c>RETURNING</c> cannot drift from one that returns it through a <c>SELECT</c>.
    /// </summary>
    protected static string Projection(MessageTable table) =>
        $"m.{table.Id}, m.{table.EventId}, m.{table.TransactionId}, m.{table.CreatedAt}, "
        + $"m.{table.Type}, m.{table.GroupKey}, m.{table.Data}, m.{table.RetryCount}, "
        + $"m.{table.VisibleAt}, m.{table.ProcessedAt}";

    protected abstract TEntity BuildEntityFromReader(DbDataReader reader);

    // Debug rather than Information: the statement is the same on every claim, so an idle pool would log
    // the same several lines once per worker per poll delay forever
    [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "Executing SQL to claim the next Head: {Sql}")]
    private partial void LogClaimSql(string Sql);
}
