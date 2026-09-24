namespace Underground.Outbox.Data;

/// <summary>
/// The context one side's messages live in, keyed by that side's message type.
/// </summary>
/// <remarks>
/// The inbox and the outbox may live in different DbContexts. An unkeyed <see cref="IDbContext"/> would
/// resolve to whichever side registered last, and the other side's workers would claim against a context
/// that does not map their table.
/// </remarks>
/// <typeparam name="TEntity">
/// The side whose context this is. It appears in no member; what it selects is the registration.
/// </typeparam>
#pragma warning disable S2326 // Unused type parameters should be removed
internal sealed class MessageDbContext<TEntity>(IDbContext context) where TEntity : class, IMessage
#pragma warning restore S2326 // Unused type parameters should be removed
{
    internal IDbContext Context { get; } = context;
}
