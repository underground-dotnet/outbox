using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Depends on a service no test registers, as a Handler deployed without its configuration would.
/// </summary>
public class UnbuildableMessageHandler(IUnregisteredDependency dependency)
    : IOutboxMessageHandler<UnbuildableMessage>, IInboxMessageHandler<UnbuildableMessage>
{
    public Task HandleAsync(UnbuildableMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
        => Task.FromResult(dependency);
}
