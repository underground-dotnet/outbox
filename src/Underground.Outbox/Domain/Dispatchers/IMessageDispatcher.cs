using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatchers;

/// <summary>
/// Hands a message to the Handler that claimed its type. One implementation per <c>DbContext</c>, emitted
/// by the source generator, so the set of message types a module's worker can dispatch is exactly the set
/// that module declares.
/// </summary>
/// <typeparam name="TContext">The context whose Handlers this dispatcher knows.</typeparam>
/// <typeparam name="TEntity">The message entity being dispatched.</typeparam>
#pragma warning disable S2326 // Unused type parameters should be removed
public interface IMessageDispatcher<TContext, in TEntity>
#pragma warning restore S2326 // Unused type parameters should be removed
    where TContext : DbContext
    where TEntity : class, IMessage
{
    /// <summary>Invokes the Handler registered for <paramref name="message"/>'s stored type.</summary>
    public Task ExecuteAsync(IServiceScope scope, TEntity message, CancellationToken cancellationToken);
}
