using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Attributes;

/// <summary>
/// Binds an <see cref="IOutboxMessageHandler{T}"/> to the outbox of one <see cref="DbContext"/>:
/// <c>[OutboxHandler&lt;OrdersContext&gt;]</c>. Required - a Handler without it is reported as
/// <c>OUTBOX002</c>, because nothing would ever dispatch it.
/// </summary>
/// <remarks>
/// The binding is stated on the Handler rather than inferred from its project, so that a Handler in a
/// referenced assembly lands under its own context and two modules may handle the same contract type.
/// See <c>docs/adr/0008-outboxes-are-bound-to-a-dbcontext.md</c>.
/// </remarks>
/// <typeparam name="TContext">The context whose outbox this Handler serves.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
// S2326: the type parameter is the whole payload - the source generator reads it to group handlers
#pragma warning disable S2326 // Unused type parameters should be removed
public sealed class OutboxHandlerAttribute<TContext> : Attribute where TContext : DbContext;
#pragma warning restore S2326
