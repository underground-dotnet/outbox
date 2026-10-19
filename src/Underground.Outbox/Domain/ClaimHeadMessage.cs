using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal abstract class ClaimHeadMessage<TEntity>(IDbContext dbContext, ILoggerFactory loggerFactory) where TEntity : class, IMessage
{
    private readonly ILogger<Claim<TEntity>> _claimLogger = loggerFactory.CreateLogger<Claim<TEntity>>();

    /// <summary>
    /// How many of the oldest pending messages the claim looks through before it considers every Group.
    /// Bounds the claim's cost whenever the answer is near the front.
    /// </summary>
    internal const int HeadMessageWindow = 1000;

    /// <summary>
    /// Claims the HeadMessage - the oldest Stable message not yet completed - of whichever Group offers the oldest one,
    /// or <c>null</c> when no Group offers anything. What holding the Claim means is the subclass's
    /// answer: the inbox keeps the row lock, the outbox commits a Lease.
    /// </summary>
    /// <remarks>
    /// One statement with two ways of finding the answer. It looks first among the oldest pending messages,
    /// which is where the answer almost always is, at a cost that does not grow with the backlog. Only when
    /// nothing there can be claimed and messages lie beyond them does it consider every Group, whose cost
    /// does. See ADR 0010.
    /// </remarks>
    internal async Task<Claim<TEntity>?> ExecuteAsync(CancellationToken cancellationToken)
    {
        // fresh per call: an NpgsqlParameter belongs to one command at a time
        List<NpgsqlParameter> parameters = [];
        AddParameters(parameters);

        // AsNoTracking: a tracked copy would let an application's SaveChanges inside the handler write
        // the message behind the Claim's guarded writes.
        // ToListAsync rather than FirstOrDefaultAsync: any LINQ operator composed onto FromSqlRaw makes EF
        // wrap the statement in a subquery, which is invalid around the outbox's data-modifying CTE.
        var claimed = await dbContext.Set<TEntity>()
            .FromSqlRaw(Statement, [.. parameters])
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return claimed.Count > 0 ? new Claim<TEntity>(claimed[0], dbContext, _claimLogger) : null;
    }

    /// <summary>
    /// The statement this side claims with, built by <see cref="BuildStatement"/>. It must return every mapped
    /// column, since EF materialises the entity from the result set by column name.
    /// </summary>
    protected abstract string Statement { get; }

    /// <summary>
    /// Binds whatever <see cref="Statement"/> parameterised. HeadMessage discovery itself takes no parameters.
    /// </summary>
    protected virtual void AddParameters(List<NpgsqlParameter> parameters)
    {
        // nothing to bind by default
    }

    /// <summary>
    /// Puts <paramref name="actOnClaimed"/> - the statement acting on the one row in a CTE named <c>claimed</c>,
    /// already locked for the calling transaction - behind the two ways of finding that row.
    /// </summary>
    /// <param name="actOnClaimed">The statement acting on the claimed row.</param>
    /// <param name="scopeColumn">
    /// A column every row read must equal the like-named parameter in, confining the claim to one Inbox;
    /// <see langword="null"/> to claim across the whole table.
    /// </param>
    protected static string BuildStatement(string actOnClaimed, string? scopeColumn = null)
    {
        string InScope(string alias) => scopeColumn is null ? "" : $"AND {alias}.{scopeColumn} = @{scopeColumn}";

        return $"WITH {WindowedHeadMessageCtes(InScope)},\n{AllGroupsHeadMessageCtes(InScope)},\n{ClaimedCte(InScope)}\n{actOnClaimed}";
    }

    /// <summary>
    /// HeadMessage discovery among the <see cref="HeadMessageWindow"/> oldest Stable messages not yet completed,
    /// walked oldest first and stopping at the first one that can be claimed, as two CTEs named
    /// <c>window_end</c> and <c>claimed_within_window</c>.
    /// </summary>
    private static string WindowedHeadMessageCtes(Func<string, string> inScope)
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
        // completed_at IS NULL is repeated on m, and the CTE returns the whole row, for the same reasons as
        // below: FOR UPDATE re-evaluates the predicates against the new row version.
        return $"""
            window_end AS MATERIALIZED (
                SELECT transaction_id, id FROM (
                    SELECT w.transaction_id, w.id
                    FROM {TEntity.TableName} w
                    WHERE w.completed_at IS NULL
                    {inScope("w")}
                    AND w.transaction_id < pg_snapshot_xmin(pg_current_snapshot())
                    ORDER BY w.transaction_id, w.id
                    LIMIT {HeadMessageWindow}
                ) oldest
                ORDER BY transaction_id DESC, id DESC
                LIMIT 1
            ),
            claimed_within_window AS (
                SELECT m.*
                FROM {TEntity.TableName} m
                WHERE m.completed_at IS NULL
                {inScope("m")}
                AND (m.transaction_id, m.id) <= ((SELECT transaction_id FROM window_end), (SELECT id FROM window_end))
                AND m.visible_at <= clock_timestamp()
                AND m.id = (
                    SELECT e.id
                    FROM {TEntity.TableName} e
                    WHERE e.group_key = m.group_key
                    AND e.completed_at IS NULL
                    {inScope("e")}
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
    /// The HeadMessage discovery every Group takes part in, as two CTEs named <c>head_messages</c> and
    /// <c>claimed_across_all_groups</c>.
    /// The second yields at most one row, already locked for the calling transaction. The table is unqualified
    /// so the schema is the deployment's to choose through <c>search_path</c>; see
    /// <c>docs/adr/0005-fixed-table-and-column-names.md</c>.
    /// </summary>
    private static string AllGroupsHeadMessageCtes(Func<string, string> inScope)
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
        // new row version; without it, a concurrent completion hands out a handled message. It returns
        // the whole row because only its own output is that new version - anything joined back to the
        // table afterwards reads the statement's snapshot, from before a concurrent retry.
        //
        // clock_timestamp() rather than now(), which is frozen for the transaction the inbox holds open.
        return $"""
            head_messages AS MATERIALIZED (
                SELECT DISTINCT ON (g.group_key) g.id, g.transaction_id
                FROM {TEntity.TableName} g
                WHERE g.completed_at IS NULL
                {inScope("g")}
                AND g.transaction_id < pg_snapshot_xmin(pg_current_snapshot())
                ORDER BY g.group_key, g.transaction_id, g.id
            ),
            claimed_across_all_groups AS (
                SELECT m.*
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
    /// The CTE named <c>claimed</c>: the claim within the window or, when that finds nothing and the window did
    /// not hold every Stable pending message, the claim across every Group.
    /// </summary>
    private static string ClaimedCte(Func<string, string> inScope)
    {
        // A WITH query is evaluated only as far as its reader fetches, and the gate is a one-time filter, so the
        // claim across every Group runs only when the window claimed nothing and a Stable message lies beyond it.
        // Without one, the window held every Stable pending message and that claim could only agree. Planning
        // still pays for it on every claim; ADR 0010 records that trade.
        //
        // The probe beyond the window is ORDER BY ... LIMIT 1, one probe of the (transaction_id, id) index,
        // because as EXISTS the planner may turn it into a scan of the whole table.
        //
        // The rows pass through as the locking CTEs returned them and are never joined back to the table, for
        // the reason given on claimed_across_all_groups.
        return $"""
            claimed AS (
                SELECT * FROM claimed_within_window
                UNION ALL
                SELECT * FROM claimed_across_all_groups
                WHERE NOT EXISTS (SELECT FROM claimed_within_window)
                AND (
                    SELECT b.id
                    FROM {TEntity.TableName} b
                    WHERE b.completed_at IS NULL
                    {inScope("b")}
                    AND b.transaction_id < pg_snapshot_xmin(pg_current_snapshot())
                    AND (b.transaction_id, b.id) > ((SELECT transaction_id FROM window_end), (SELECT id FROM window_end))
                    ORDER BY b.transaction_id, b.id
                    LIMIT 1
                ) IS NOT NULL
                LIMIT 1
            )
            """;
    }
}
