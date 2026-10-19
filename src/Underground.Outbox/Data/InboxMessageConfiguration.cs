using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Underground.Outbox.Data;

/// <summary>
/// Applies <see cref="MessageConfiguration{TEntity}"/> to <see cref="InboxMessage"/>, with every index
/// scoped to one Inbox.
/// </summary>
internal sealed class InboxMessageConfiguration : MessageConfiguration<InboxMessage>
{
    protected override string[] LeadingIndexColumns => [nameof(InboxMessage.Inbox)];

    public override void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        base.Configure(builder);

        // a row written without going through staging - by an external producer, or by adding to the DbSet
        // directly - would otherwise belong to an Inbox nobody claims from, and wait there forever
        builder.ToTable(table => table.HasCheckConstraint("ck_inbox_inbox_not_empty", "\"inbox\" <> ''"));
    }
}
