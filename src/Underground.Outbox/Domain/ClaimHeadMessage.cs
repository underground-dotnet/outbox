using Microsoft.EntityFrameworkCore;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal abstract class ClaimHeadMessage<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
    /// <summary>
    /// How many of the oldest pending messages the first statement looks through before falling back to
    /// every Group. Bounds that statement's cost when nothing near the front can be claimed.
    /// </summary>
    internal const int HeadMessageWindow = 1000;

    /// <summary>
    /// Claims the HeadMessage - the oldest Stable message not yet completed - of whichever Group offers the oldest one,
    /// or <c>null</c> when no Group offers anything. What holding the claim means is the subclass's
    /// answer: the inbox keeps the row lock, the outbox commits a Lease.
    /// </summary>
    /// <remarks>
    /// Two statements with one answer. The first looks only among the oldest pending messages, which is where
    /// the answer almost always is, and costs the same however long the backlog. Only when nothing there
    /// can be claimed does the second consider every Group, whose cost grows with the backlog. See ADR 0010.
    /// </remarks>
    internal async Task<TEntity?> ExecuteAsync(CancellationToken cancellationToken)
    {
        return await ClaimAsync(Statements.WithinWindow, cancellationToken).ConfigureAwait(false)
            ?? await ClaimAsync(Statements.AcrossAllGroups, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TEntity?> ClaimAsync(string sql, CancellationToken cancellationToken)
    {
        // fresh per statement: an NpgsqlParameter belongs to one command at a time
        List<NpgsqlParameter> parameters = [];
        AddParameters(parameters);

        // AsNoTracking: a tracked copy would let an application's SaveChanges inside the handler write
        // the message behind the guarded writes' back.
        // ToListAsync rather than FirstOrDefaultAsync: any LINQ operator composed onto FromSqlRaw makes EF
        // wrap the statement in a subquery, which is invalid around the outbox's data-modifying CTE.
        var claimed = await dbContext.Set<TEntity>()
            .FromSqlRaw(sql, [.. parameters])
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return claimed.Count > 0 ? claimed[0] : null;
    }

    /// <summary>
    /// The two statements this side claims with, built by <see cref="BuildStatements"/>. Each must return
    /// every mapped column, since EF materialises the entity from the result set by column name.
    /// </summary>
    protected abstract ClaimStatements Statements { get; }

    /// <summary>
    /// Binds whatever <see cref="Statements"/> parameterised. HeadMessage discovery itself takes no parameters.
    /// </summary>
    protected virtual void AddParameters(List<NpgsqlParameter> parameters)
    {
        // nothing to bind by default
    }

    /// <summary>
    /// Puts <paramref name="actOnClaimed"/> - the statement acting on the one id in a CTE named <c>claimed</c>,
    /// already locked for the calling transaction - behind each of the two ways of finding that id.
    /// </summary>
    protected static ClaimStatements BuildStatements(string actOnClaimed)
        => new($"{WindowedHeadMessageCte()}\n{actOnClaimed}", $"{AllGroupsHeadMessageCte()}\n{actOnClaimed}");

    /// <summary>
    /// HeadMessage discovery among the <see cref="HeadMessageWindow"/> oldest Stable messages not yet completed,
    /// walked oldest first and stopping at the first one that can be claimed.
    /// </summary>
    private static string WindowedHeadMessageCte()
    {
        // Exact rather than approximate: the window holds every Stable pending message up to its last one,
        // so if any message in it can be claimed, the oldest one that can be claimed anywhere is in it too.
        //
        // The window's end is passed as scalar subqueries so that it becomes an index condition, which is
        // what stops the scan there; as a join qual it is only a filter and the scan runs to the end.
        //
        // The head test is a scalar subquery with LIMIT 1 - one probe of the Group's index per candidate -
        // because as NOT EXISTS the planner may turn it into an anti-join over the whole table. A message
        // passing it is Stable, so the earlier one it is compared against is too.
        //
        // completed_at IS NULL is repeated on m for the same reason as below: FOR UPDATE re-evaluates the
        // predicates against the new row version.
        return $"""
            WITH window_end AS MATERIALIZED (
                SELECT transaction_id, id FROM (
                    SELECT transaction_id, id
                    FROM {TEntity.TableName}
                    WHERE completed_at IS NULL
                    AND transaction_id < pg_snapshot_xmin(pg_current_snapshot())
                    ORDER BY transaction_id, id
                    LIMIT {HeadMessageWindow}
                ) oldest
                ORDER BY transaction_id DESC, id DESC
                LIMIT 1
            ),
            claimed AS (
                SELECT m.id
                FROM {TEntity.TableName} m
                WHERE m.completed_at IS NULL
                AND (m.transaction_id, m.id) <= ((SELECT transaction_id FROM window_end), (SELECT id FROM window_end))
                AND m.visible_at <= clock_timestamp()
                AND m.id = (
                    SELECT e.id
                    FROM {TEntity.TableName} e
                    WHERE e.group_key = m.group_key
                    AND e.completed_at IS NULL
                    ORDER BY e.transaction_id, e.id
                    LIMIT 1
                )
                ORDER BY m.transaction_id, m.id
                LIMIT 1
                FOR UPDATE OF m SKIP LOCKED
            )
            """;
    }

    /// <summary>
    /// The HeadMessage discovery every Group takes part in, as two CTEs named <c>head_messages</c> and <c>claimed</c>.
    /// The second yields at most one id, already locked for the calling transaction. The table is unqualified
    /// so the schema is the deployment's to choose through <c>search_path</c>; see
    /// <c>docs/adr/0005-fixed-table-and-column-names.md</c>.
    /// </summary>
    private static string AllGroupsHeadMessageCte()
    {
        // Two steps, because a Group's HeadMessage is its lowest (transaction_id, id) *regardless of
        // visibility*; only then is visibility tested. Filtering by visible_at first would hand out the
        // message behind a HeadMessage in backoff, which is the reordering this design exists to prevent.
        //
        // Ordering by (transaction_id, id) rather than id alone: identity values are handed out at insert,
        // not at commit, so a transaction starting later but committing first would otherwise win. The
        // stable filter is safe here for the same reason - an unstable row sorts after every stable one.
        //
        // pg_snapshot_xmin is cluster-wide - transaction ids are shared by every database on the instance - so the
        // stable filter costs what ADR 0002 records: any open write transaction withholds every message newer than
        // it, from both sides and every Group, in every database on that instance. The inbox is one
        // of those writers for the length of its Handler - FOR UPDATE below assigns it a real xid.
        //
        // head_messages is MATERIALIZED and sorted by its own columns so that the planner cannot walk the
        // (transaction_id, id) index for m and test every row against every HeadMessage, which it otherwise
        // prefers and which is quadratic.
        //
        // FOR UPDATE cannot be combined with DISTINCT ON, hence the second CTE. SKIP LOCKED so a HeadMessage
        // another worker holds is passed over rather than aborting a statement spanning every Group.
        // It repeats completed_at IS NULL because FOR UPDATE re-evaluates only that predicate against the
        // new row version; without it, a concurrent completion hands out a handled message.
        //
        // clock_timestamp() rather than now(), which is frozen for the transaction the inbox holds open.
        return $"""
            WITH head_messages AS MATERIALIZED (
                SELECT DISTINCT ON (group_key) id, transaction_id
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
                ORDER BY h.transaction_id, h.id
                LIMIT 1
                FOR UPDATE OF m SKIP LOCKED
            )
            """;
    }

    /// <summary>
    /// One side's claim statement behind each of the two ways of finding the HeadMessage to claim.
    /// </summary>
    /// <param name="WithinWindow">Looks among the oldest pending messages only; tried first.</param>
    /// <param name="AcrossAllGroups">Considers every Group; tried when the first finds nothing.</param>
    protected sealed record ClaimStatements(string WithinWindow, string AcrossAllGroups);
}
