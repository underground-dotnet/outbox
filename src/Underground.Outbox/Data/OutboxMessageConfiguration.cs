namespace Underground.Outbox.Data;

/// <summary>
/// Applies <see cref="MessageConfiguration{TEntity}"/> to <see cref="OutboxMessage"/>.
/// </summary>
internal sealed class OutboxMessageConfiguration : MessageConfiguration<OutboxMessage>;
