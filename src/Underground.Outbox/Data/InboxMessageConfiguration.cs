namespace Underground.Outbox.Data;

/// <summary>
/// Applies <see cref="MessageConfiguration{TEntity}"/> to <see cref="InboxMessage"/>, whose Groups belong to one
/// Recipient each (ADR 0012).
/// </summary>
internal sealed class InboxMessageConfiguration : MessageConfiguration<InboxMessage>
{
    protected override string[] GroupProperties => [nameof(InboxMessage.Recipient), nameof(IMessage.GroupKey)];
}
