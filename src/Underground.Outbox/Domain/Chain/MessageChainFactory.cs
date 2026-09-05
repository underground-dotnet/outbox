using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Assembles the chain each side runs. It is internal and takes no options on purpose: the order
/// between stages is a correctness property, not a preference a consumer should be able to express.
/// </summary>
/// <remarks>
/// <para>The order, outermost first, and why:</para>
/// <list type="bullet">
/// <item><see cref="LogMessageStage{TEntity}"/> outermost, so that the message is announced whatever
/// becomes of it.</item>
/// <item><see cref="RecordSuccessStage{TEntity}"/> outside <see cref="RecordFailureStage{TEntity}"/>, so
/// that it sees the <c>false</c> a recorded failure reports and stands aside. Inside it, a completion write
/// that threw would be caught and turned into a retry - right for the inbox, which rolls back, and wrong
/// for the outbox, whose effect already happened.</item>
/// <item><see cref="RecordFailureStage{TEntity}"/> outside the savepoint, because the attempt
/// bookkeeping it writes must survive the rollback that discards the failed Handler's writes.</item>
/// <item><see cref="SavepointStage{TEntity}"/> around the dispatch whose writes it isolates - and
/// absent from the outbox, which holds no transaction to roll back.</item>
/// <item><see cref="TimeoutStage{TEntity}"/> innermost, so that the Handler and the save that follows
/// it are the only things running on the narrowed token; the rollback and the bookkeeping a timeout leads
/// to are outside it, and so still have a live token to run on. Both outcome writes are outside it for the
/// same reason - a Handler that spent its whole budget must still be able to record what happened.</item>
/// </list>
/// </remarks>
internal static class MessageChainFactory
{
    /// <summary>
    /// The inbox chain. It runs inside the transaction that the outcome is recorded in, so a failed
    /// Handler's writes have a savepoint to be rolled back to.
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
    /// The outbox chain. No savepoint: an outbox worker dispatches with no transaction open, so there is
    /// nothing to roll back to. A Handler that writes to this database and then fails therefore keeps
    /// those writes - an outbox Handler's business is an effect outside this database, and one that also
    /// writes to it is asking for the inbox.
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
