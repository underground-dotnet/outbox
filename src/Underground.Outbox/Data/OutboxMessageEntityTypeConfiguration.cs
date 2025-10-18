using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Underground.Outbox.Data;

public class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        // builder.ToTable("outbox_messages");
        builder.HasKey(e => e.Id);
        // builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.Property(e => e.EventId).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.Type).IsRequired();
        builder.Property(e => e.Data).IsRequired();
    }
}
