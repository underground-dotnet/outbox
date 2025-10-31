using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Underground.Outbox.Data;

namespace ConsoleApp;

public class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        // Demo on how to override table name
        builder.ToTable("outbox_msgs");
    }
}
