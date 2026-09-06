using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Assembles the pipeline each side runs. Internal and without options on purpose: the order between middleware
/// is a correctness property, not a preference.
/// </summary>
/// <remarks>
/// <para>The order, outermost first, and why:</para>
/// <list type="bullet">
/// <item><see cref="LogMessageMiddleware{TEntity}"/> outermost, so the message is announced whatever becomes
/// of it.</item>
/// <item><see cref="RecordSuccessMiddleware{TEntity}"/> outside <see cref="RecordFailureMiddleware{TEntity}"/>, so
/// it stands aside for a recorded failure instead of having its own completion write turned into a
/// retry.</item>
/// <item><see cref="RecordFailureMiddleware{TEntity}"/> outside the savepoint, so its attempt bookkeeping
/// survives the rollback.</item>
/// <item><see cref="SavepointMiddleware{TEntity}"/> around the dispatch whose writes it isolates - absent from
/// the outbox, which holds no transaction.</item>
/// <item><see cref="TimeoutMiddleware{TEntity}"/> innermost, so the rollback and both outcome writes still
/// have a live token once the Handler has spent its budget.</item>
/// </list>
/// </remarks>
internal static class MessagePipelineFactory
{
    /// <summary>
    /// The inbox pipeline. It runs inside the transaction the outcome is recorded in, so a failed Handler's
    /// writes have a savepoint to be rolled back to.
    /// </summary>
    internal static MessagePipeline<InboxMessage> CreateInbox(IServiceProvider services)
        => new(
            [
                services.GetRequiredService<LogMessageMiddleware<InboxMessage>>(),
                services.GetRequiredService<RecordSuccessMiddleware<InboxMessage>>(),
                services.GetRequiredService<RecordFailureMiddleware<InboxMessage>>(),
                services.GetRequiredService<SavepointMiddleware<InboxMessage>>(),
                services.GetRequiredService<TimeoutMiddleware<InboxMessage>>(),
            ],
            services.GetRequiredService<DispatchMessage<InboxMessage>>());

    /// <summary>
    /// The outbox pipeline. No savepoint: an outbox worker dispatches with no transaction open, so a Handler
    /// that writes to this database and then fails keeps those writes.
    /// </summary>
    internal static MessagePipeline<OutboxMessage> CreateOutbox(IServiceProvider services)
        => new(
            [
                services.GetRequiredService<LogMessageMiddleware<OutboxMessage>>(),
                services.GetRequiredService<RecordSuccessMiddleware<OutboxMessage>>(),
                services.GetRequiredService<RecordFailureMiddleware<OutboxMessage>>(),
                services.GetRequiredService<TimeoutMiddleware<OutboxMessage>>(),
            ],
            services.GetRequiredService<DispatchMessage<OutboxMessage>>());
}
