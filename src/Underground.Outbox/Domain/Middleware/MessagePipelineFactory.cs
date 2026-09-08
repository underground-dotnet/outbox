using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Assembles the pipeline each side runs. Internal and without options on purpose: the order between middleware
/// is a correctness property, not a preference.
/// </summary>
internal static class MessagePipelineFactory
{
    /// <summary>
    /// The inbox pipeline. It runs inside the transaction the outcome is recorded in, so a failed Handler's
    /// writes have a savepoint to be rolled back to.
    /// </summary>
    internal static MessagePipeline<InboxMessage> CreateInbox(IServiceProvider services)
        => new(
            [
                services.GetRequiredService<TraceMessageMiddleware<InboxMessage>>(),
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
                services.GetRequiredService<TraceMessageMiddleware<OutboxMessage>>(),
                services.GetRequiredService<LogMessageMiddleware<OutboxMessage>>(),
                services.GetRequiredService<RecordSuccessMiddleware<OutboxMessage>>(),
                services.GetRequiredService<RecordFailureMiddleware<OutboxMessage>>(),
                services.GetRequiredService<TimeoutMiddleware<OutboxMessage>>(),
            ],
            services.GetRequiredService<DispatchMessage<OutboxMessage>>());
}
