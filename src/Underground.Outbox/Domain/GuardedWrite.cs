using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// A write that ends a claimed message's turn - it was handled, or the attempt failed - guarded on the
/// Lease instant the claim granted. Matching no row means the Lease was lost, which is reported and not
/// thrown: the effect has already happened and there is nothing for a caller to recover.
/// </summary>
/// <remarks>
/// <para>
/// The guard is written the same way on both sides. On the inbox it is trivially satisfied - the row is
/// locked for the whole transaction, so nothing can have moved its visibility instant - and a predicate
/// that is always true is cheaper than giving each side its own write path.
/// </para>
/// <para>
/// Subclasses supply only their statement and any parameters beyond the two the guard itself needs, so
/// that a new outcome cannot be added with the guard accidentally left off.
/// </para>
/// </remarks>
internal abstract partial class GuardedWrite<TEntity>(IDbContext dbContext, ILogger logger) where TEntity : class, IMessage
{
    // keyed by concrete type as well as by model, because every subclass closed over the same TEntity
    // shares this field and they do not share a statement
#pragma warning disable S2743 // A static field in a generic type is not shared among instances of different close constructed types.
    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<Type, string>> SqlByModel = [];
#pragma warning restore S2743 // A static field in a generic type is not shared among instances of different close constructed types.

    /// <summary>
    /// Performs the write if this worker still holds the message.
    /// </summary>
    /// <returns>
    /// Whether the write landed. <c>false</c> means the Lease was lost - the message is now some other
    /// worker's, nothing here may touch it, and the loss has been logged.
    /// </returns>
    internal async Task<bool> ExecuteAsync(TEntity message, CancellationToken cancellationToken)
    {
        var sql = SqlByModel.GetOrCreateValue(dbContext.Model)
            .GetOrAdd(GetType(), _ => BuildSql(MessageTable.For<TEntity>(dbContext.Model)));

        List<NpgsqlParameter> parameters =
        [
            new("id", message.Id),
            new("lease", message.VisibleAt),
        ];
        AddParameters(parameters, message);

        var rows = await dbContext.Database
            .ExecuteSqlRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);

        if (rows != 0)
        {
            return true;
        }

        LogLeaseLost(logger, message.Id);
        return false;
    }

    /// <summary>
    /// The statement to run, which must end in <see cref="Guard"/>.
    /// </summary>
    /// <param name="table">
    /// The table's identifiers, read off the EF model rather than written literally, since a consumer can
    /// remap any of them through EF Core mappings.
    /// </param>
    protected abstract string BuildSql(MessageTable table);

    /// <summary>
    /// Binds anything the statement needs beyond <c>@id</c> and <c>@lease</c>, which are already there.
    /// </summary>
    protected virtual void AddParameters(List<NpgsqlParameter> parameters, TEntity message)
    {
        // the guard's own two parameters are all most writes need
    }

    /// <summary>
    /// The predicate that makes a write this worker's to make: this message, and only while the Lease
    /// granted at claim time is still the one on the row.
    /// </summary>
    protected static string Guard(MessageTable table) =>
        $"""
        WHERE {table.Id} = @id
        AND {table.VisibleAt} = @lease
        """;

    // A warning rather than an exception: the Lease expired, another worker has since claimed the
    // message, and this worker's outcome is simply discarded. Nothing is wrong with the system - it is how
    // at-least-once delivery is bounded - but it means an effect was carried out twice, which an operator
    // wants to see the rate of. The logger comes from the subclass, so the category says which outcome
    // was lost.
    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Warning,
        Message = "Lost the Lease on message {MessageId}: it expired before this worker finished, so another worker owns the message and this outcome was discarded")]
    private static partial void LogLeaseLost(ILogger logger, long messageId);
}
