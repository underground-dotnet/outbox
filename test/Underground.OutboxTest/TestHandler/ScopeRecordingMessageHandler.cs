using System.Data;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Records the <see cref="ScopeProbe"/> of the scope it ran in, then fails so an exception policy runs.
/// </summary>
public class ScopeRecordingMessageHandler(ScopeProbe probe) : IOutboxMessageHandler<ScopeRecordingMessage>
{
    public Task HandleAsync(ScopeRecordingMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        RecordedScopes.Handler.Enqueue(probe);
        throw new DataException("Failed so the exception policy runs");
    }
}
