namespace Underground.Outbox.Data;

/// <summary>
/// Applies <see cref="MessageConfiguration{TEntity}"/> to <see cref="InboxMessage"/>.
/// </summary>
internal sealed class InboxMessageConfiguration : MessageConfiguration<InboxMessage>;
