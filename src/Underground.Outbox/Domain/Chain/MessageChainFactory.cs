using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Assembles the chain each side runs. Internal and without options on purpose: the order between stages
/// is a correctness property, not a preference.
/// </summary>
/// <remarks>
/// <para>The order, outermost first, and why:</para>
/// <list type="bullet">
/// <item><see cref="LogMessageStage{TEntity}"/> outermost, so the message is announced whatever becomes
/// of it.</item>
/// <item><see cref="RecordSuccessStage{TEntity}"/> outside <see cref="RecordFailureStage{TEntity}"/>, so
/// it stands aside for a recorded failure instead of having its own completion write turned into a
/// retry.</item>
/// <item><see cref="RecordFailureStage{TEntity}"/> outside the savepoint, so its attempt bookkeeping
/// survives the rollback.</item>
/// <item><see cref="SavepointStage{TEntity}"/> around the dispatch whose writes it isolates - absent from
/// the outbox, which holds no transaction.</item>
/// <item><see cref="TimeoutStage{TEntity}"/> innermost, so the rollback and both outcome writes still
/// have a live token once the Handler has spent its budget.</item>
/// </list>
/// </remarks>
internal static class MessageChainFactory
{
    /// <summary>
    /// The inbox chain. It runs inside the transaction the outcome is recorded in, so a failed Handler's
    /// writes have a savepoint to be rolled back to.
    /// </summary>
    internal static MessageChain<InboxMessage> CreateInbox(IServiceProvider services)
        => new(
            [
                services.GetRequiredService<LogMessageStage<InboxMessage>>(),
                services.GetRequiredService<RecordSuccessStage<InboxMessage>>(),
                services.GetRequiredService<RecordFailureStage<InboxMessage>>(),
                services.GetRequiredService<SavepointStage<InboxMessage>>(),
                services.GetRequiredService<TimeoutStage<InboxMessage>>(),
            ],
            services.GetRequiredService<DispatchMessage<InboxMessage>>());

    /// <summary>
    /// The outbox chain. No savepoint: an outbox worker dispatches with no transaction open, so a Handler
    /// that writes to this database and then fails keeps those writes.
    /// </summary>
    internal static MessageChain<OutboxMessage> CreateOutbox(IServiceProvider services)
        => new(
            [
                services.GetRequiredService<LogMessageStage<OutboxMessage>>(),
                services.GetRequiredService<RecordSuccessStage<OutboxMessage>>(),
                services.GetRequiredService<RecordFailureStage<OutboxMessage>>(),
                services.GetRequiredService<TimeoutStage<OutboxMessage>>(),
            ],
            services.GetRequiredService<DispatchMessage<OutboxMessage>>());
}
