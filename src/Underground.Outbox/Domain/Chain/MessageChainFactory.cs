using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Assembles the one chain both sides run. It is internal and takes no options on purpose: the order
/// between stages is a correctness property, not a preference a consumer should be able to express.
/// </summary>
internal static class MessageChainFactory
{
    /// <summary>
    /// Builds the chain, outermost stage first.
    /// </summary>
    internal static MessageChain<TEntity> Create<TEntity>(IServiceProvider services) where TEntity : class, IMessage
        => new(
            [
                // outermost, so that the message is announced whatever becomes of it
                services.GetRequiredService<LogMessageStage<TEntity>>(),

                // outside the savepoint, because the attempt bookkeeping it writes must survive the
                // rollback that discards the failed Handler's writes
                services.GetRequiredService<RecordFailureStage<TEntity>>(),

                // innermost, next to the dispatch whose writes it isolates. Absent from a side that holds
                // no transaction across the dispatch and so has nothing to roll back to; today both sides
                // hold one, so both sides get it.
                services.GetRequiredService<SavepointStage<TEntity>>(),
            ],
            services.GetRequiredService<DispatchMessage<TEntity>>());
}
