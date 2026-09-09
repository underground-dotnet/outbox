using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// Wakes the workers of one registered inbox or outbox. Separate from <see cref="IOutbox{TContext}"/> so
/// that the save-changes interceptor - which knows only that its context is a <see cref="DbContext"/> -
/// can signal a side without naming the side's own registration constraint.
/// </summary>
/// <typeparam name="TContext">The context this inbox or outbox belongs to.</typeparam>
/// <typeparam name="TEntity">The message entity this side stores.</typeparam>
#pragma warning disable S2326 // Unused type parameters should be removed
internal interface IWorkSignal<TContext, TEntity>
#pragma warning restore S2326 // Unused type parameters should be removed
    where TContext : DbContext
    where TEntity : class, IMessage
{
    /// <summary>Reports that work may have appeared. Never blocks, never fails.</summary>
    void ProcessMessages();
}
