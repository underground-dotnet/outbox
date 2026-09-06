using Microsoft.EntityFrameworkCore;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal abstract class ClaimHead<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
    /// <summary>
    /// Claims the Head - the oldest settled unhandled message - of whichever Group offers the oldest one,
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
    /// The statement this side claims with, built around <see cref="LockedHeadCte"/>. It must return every
    /// mapped column, since EF materialises the entity from the result set by column name.
    /// </summary>
    protected abstract string Sql { get; }

    /// <summary>
    /// Binds whatever <see cref="Sql"/> parameterised. Head discovery itself takes no parameters.
    /// </summary>
    protected virtual void AddParameters(List<NpgsqlParameter> parameters)
    {
        // nothing to bind by default
    }

    /// <summary>
    /// The Head discovery both sides share, as two CTEs named <c>heads</c> and <c>claimed</c>. The second
    /// yields at most one id, already locked for the calling transaction; a caller appends the statement
    /// that acts on it. The table is unqualified so the schema is the deployment's to choose through
    /// <c>search_path</c>; see <c>docs/adr/0005-fixed-table-and-column-names.md</c>.
    /// </summary>
    protected static string LockedHeadCte()
    {
        // Two stages, because a Group's Head is its lowest (transaction_id, id) *regardless of
        // visibility*; only then is visibility tested. Filtering by visible_at first would hand out the
        // message behind a Head in backoff, which is the reordering this design exists to prevent.
        //
        // Ordering by (transaction_id, id) rather than id alone: identity values are handed out at insert,
        // not at commit, so a transaction starting later but committing first would otherwise win. The
        // settled filter is safe here for the same reason - an unsettled row sorts after every settled one.
        //
        // FOR UPDATE cannot be combined with DISTINCT ON, hence the second CTE. SKIP LOCKED so a Head
        // another worker holds is passed over rather than aborting a statement spanning every Group.
        // It repeats processed_at IS NULL because FOR UPDATE re-evaluates only that predicate against the
        // new row version; without it, a concurrent completion hands out a handled message.
        //
        // clock_timestamp() rather than now(), which is frozen for the transaction the inbox holds open.
        return $"""
            WITH heads AS (
                SELECT DISTINCT ON (group_key) id
                FROM {TEntity.TableName}
                WHERE processed_at IS NULL
                AND transaction_id < pg_snapshot_xmin(pg_current_snapshot())
                ORDER BY group_key, transaction_id, id
            ),
            claimed AS (
                SELECT m.id
                FROM heads h
                JOIN {TEntity.TableName} m ON m.id = h.id
                WHERE m.processed_at IS NULL
                AND m.visible_at <= clock_timestamp()
                ORDER BY m.transaction_id, m.id
                LIMIT 1
                FOR UPDATE OF m SKIP LOCKED
            )
            """;
    }
}
