using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Assembles the pipeline each side runs, once per registered inbox or outbox. Internal and without options
/// on purpose: the order between middleware is a correctness property, not a preference.
/// </summary>
internal static class MessagePipelineFactory
{
    /// <summary>
    /// The inbox pipeline. It runs inside the transaction the outcome is recorded in, so a failed Handler's
    /// writes have a savepoint to be rolled back to.
    /// </summary>
    internal static MessagePipeline<TContext, InboxMessage> CreateInbox<TContext>(IServiceProvider services)
        where TContext : DbContext, IInboxDbContext
        => new(
            [
                services.GetRequiredService<TraceMessageMiddleware<TContext, InboxMessage>>(),
                services.GetRequiredService<LogMessageMiddleware<TContext, InboxMessage>>(),
                services.GetRequiredService<RecordSuccessMiddleware<TContext, InboxMessage>>(),
                services.GetRequiredService<RecordFailureMiddleware<TContext, InboxMessage>>(),
                services.GetRequiredService<SavepointMiddleware<TContext, InboxMessage>>(),
                services.GetRequiredService<TimeoutMiddleware<TContext, InboxMessage>>(),
            ],
            services.GetRequiredService<DispatchMessage<TContext, InboxMessage>>());

    /// <summary>
    /// The outbox pipeline. No savepoint: an outbox worker dispatches with no transaction open, so a Handler
    /// that writes to this database and then fails keeps those writes.
    /// </summary>
    internal static MessagePipeline<TContext, OutboxMessage> CreateOutbox<TContext>(IServiceProvider services)
        where TContext : DbContext, IOutboxDbContext
        => new(
            [
                services.GetRequiredService<TraceMessageMiddleware<TContext, OutboxMessage>>(),
                services.GetRequiredService<LogMessageMiddleware<TContext, OutboxMessage>>(),
                services.GetRequiredService<RecordSuccessMiddleware<TContext, OutboxMessage>>(),
                services.GetRequiredService<RecordFailureMiddleware<TContext, OutboxMessage>>(),
                services.GetRequiredService<TimeoutMiddleware<TContext, OutboxMessage>>(),
            ],
            services.GetRequiredService<DispatchMessage<TContext, OutboxMessage>>());
}
