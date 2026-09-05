using Microsoft.EntityFrameworkCore;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal abstract class ClaimHead<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
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
        List<NpgsqlParameter> parameters = [];
        AddParameters(parameters);

        // The row is materialised by EF rather than read column by column, so the projection cannot drift
        // from the entity. Two things about this call are load-bearing:
        //
        // AsNoTracking, because the claimed message must not be tracked. Every write to it goes through a
        // GuardedWrite, and a tracked copy would let an application's SaveChanges inside the handler's
        // transaction write the message behind that guard's back.
        //
        // ToListAsync rather than FirstOrDefaultAsync, because any LINQ operator composed onto FromSqlRaw
        // makes EF wrap the statement in a subquery, which is invalid around the data-modifying CTE the
        // outbox claims with. The statement already returns at most one row.
        var claimed = await dbContext.Set<TEntity>()
            .FromSqlRaw(Sql, [.. parameters])
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return claimed.Count > 0 ? claimed[0] : null;
    }

    /// <summary>
    /// The statement this side claims with, built around <see cref="LockedHeadCte"/>. The two sides differ
    /// only in what they do with the row that CTE locks. It must return every mapped column, since EF
    /// materialises the entity from the result set by column name.
    /// </summary>
    protected abstract string Sql { get; }

    /// <summary>
    /// Binds whatever <see cref="Sql"/> parameterised. Head discovery itself takes no parameters, so this
    /// does nothing unless a side added one.
    /// </summary>
    protected virtual void AddParameters(List<NpgsqlParameter> parameters)
    {
        // nothing to bind by default
    }

    /// <summary>
    /// The Head discovery both sides share, as two CTEs named <c>heads</c> and <c>claimed</c>. The second
    /// yields at most one id, already locked for the calling transaction; a caller appends the statement
    /// that acts on it.
    /// </summary>
    /// <remarks>
    /// The table is named from <see cref="IMessage.TableName"/> rather than spelled out again here, and
    /// unqualified so that the schema is the deployment's to choose through <c>search_path</c>. See
    /// <c>docs/adr/0005-fixed-table-and-column-names.md</c>.
    /// </remarks>
    protected static string LockedHeadCte()
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
