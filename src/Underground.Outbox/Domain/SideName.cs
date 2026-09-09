using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Names one registered inbox or outbox - <c>OrdersContext outbox</c> - so that two modules' messages can
/// be told apart in one process's logs and traces.
/// </summary>
internal static class SideName
{
    internal static string For<TContext, TEntity>()
        where TContext : DbContext
        where TEntity : class, IMessage
        => $"{typeof(TContext).Name} {TEntity.TableName}";
}
