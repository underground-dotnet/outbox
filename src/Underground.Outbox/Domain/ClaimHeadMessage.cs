using Microsoft.EntityFrameworkCore;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal abstract class ClaimHeadMessage<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
    /// <summary>
    /// Claims the HeadMessage - the oldest Stable message not yet completed - of whichever Group offers the oldest one,
    /// or <c>null</c> when no Group offers anything. What holding the claim means is the subclass's
    /// answer: the inbox keeps the row lock, the outbox commits a Lease.
    /// </summary>
    internal async Task<TEntity?> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<NpgsqlParameter> parameters = [];
        AddParameters(parameters);

        // AsNoTracking: a tracked copy would let an application's SaveChanges inside the handler write
        // the message behind the guarded writes' back.
        // ToListAsync rather than FirstOrDefaultAsync: any LINQ operator composed onto FromSqlRaw makes EF
        // wrap the statement in a subquery, which is invalid around the outbox's data-modifying CTE.
        var claimed = await dbContext.Set<TEntity>()
            .FromSqlRaw(Sql, [.. parameters])
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return claimed.Count > 0 ? claimed[0] : null;
    }

    /// <summary>
    /// The statement this side claims with, built around <see cref="LockedHeadMessageCte"/>. It must return every
    /// mapped column, since EF materialises the entity from the result set by column name.
    /// </summary>
    protected abstract string Sql { get; }

    /// <summary>
    /// Binds whatever <see cref="Sql"/> parameterised. HeadMessage discovery itself takes no parameters.
    /// </summary>
    protected virtual void AddParameters(List<NpgsqlParameter> parameters)
    {
        // nothing to bind by default
    }

    /// <summary>
    /// The HeadMessage discovery both sides share, as two CTEs named <c>head_messages</c> and <c>claimed</c>. The second
    /// yields at most one id, already locked for the calling transaction; a caller appends the statement
    /// that acts on it. The table is unqualified so the schema is the deployment's to choose through
    /// <c>search_path</c>; see <c>docs/adr/0005-fixed-table-and-column-names.md</c>.
    /// </summary>
    protected static string LockedHeadMessageCte()
    {
        // Two steps, because a Group's HeadMessage is its lowest (transaction_id, id) *regardless of
        // visibility*; only then is visibility tested. Filtering by visible_at first would hand out the
        // message behind a HeadMessage in backoff, which is the reordering this design exists to prevent.
        //
        // Ordering by (transaction_id, id) rather than id alone: identity values are handed out at insert,
        // not at commit, so a transaction starting later but committing first would otherwise win. The
        // stable filter is safe here for the same reason - an unstable row sorts after every stable one.
        //
        // pg_snapshot_xmin is database-wide, so the stable filter costs what ADR 0002 records: any open write
        // transaction withholds every message newer than it, from both sides and every Group. The inbox is one
        // of those writers for the length of its Handler - FOR UPDATE below assigns it a real xid.
        //
        // FOR UPDATE cannot be combined with DISTINCT ON, hence the second CTE. SKIP LOCKED so a HeadMessage
        // another worker holds is passed over rather than aborting a statement spanning every Group.
        // It repeats completed_at IS NULL because FOR UPDATE re-evaluates only that predicate against the
        // new row version; without it, a concurrent completion hands out a handled message.
        //
        // clock_timestamp() rather than now(), which is frozen for the transaction the inbox holds open.
        return $"""
            WITH head_messages AS (
                SELECT DISTINCT ON (group_key) id
                FROM {TEntity.TableName}
                WHERE completed_at IS NULL
                AND transaction_id < pg_snapshot_xmin(pg_current_snapshot())
                ORDER BY group_key, transaction_id, id
            ),
            claimed AS (
                SELECT m.id
                FROM head_messages h
                JOIN {TEntity.TableName} m ON m.id = h.id
                WHERE m.completed_at IS NULL
                AND m.visible_at <= clock_timestamp()
                ORDER BY m.transaction_id, m.id
                LIMIT 1
                FOR UPDATE OF m SKIP LOCKED
            )
            """;
    }
}
