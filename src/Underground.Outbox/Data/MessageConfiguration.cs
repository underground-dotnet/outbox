using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Underground.Outbox.Data;

/// <summary>
/// Model configuration shared by both message tables: the database-assigned
/// <see cref="IMessage.TransactionId"/> and <see cref="IMessage.VisibleAt"/>, and the partial index
/// that serves Head lookup.
/// It is applied automatically through <see cref="EntityTypeConfigurationAttribute"/> on the entity
/// types, so a consumer's model carries it without a call they have to remember.
/// </summary>
/// <typeparam name="TEntity">The message entity being configured.</typeparam>
internal abstract class MessageConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class, IMessage
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        // Properties are addressed by name rather than through a lambda: a lambda over TEntity reaches
        // them through IMessage, and EF then identifies the property by the interface's member, where the
        // [Column] annotations are not.

        // xid8 is assigned by the database rather than the application, because the value has to identify
        // the transaction doing the insert. pg_current_xact_id() assigns the transaction one if it does
        // not have one yet.
        builder.Property(nameof(IMessage.TransactionId))
            .HasColumnType("xid8")
            .HasDefaultValueSql("pg_current_xact_id()")
            .ValueGeneratedOnAdd();

        // The default is what makes an unscheduled message deliverable at once, and leaving the column to
        // the database is what keeps that instant off the application's clock. clock_timestamp() rather
        // than now(), because now() is frozen for the transaction the inbox holds open across its handler.
        // A caller who schedules a message supplies the instant instead, and EF then includes the column
        // in the insert rather than letting the default apply.
        builder.Property(nameof(IMessage.VisibleAt))
            .HasDefaultValueSql("clock_timestamp()")
            .ValueGeneratedOnAdd();

        // Head lookup reads the lowest (TransactionId, Id) per GroupKey among unprocessed messages. The
        // filter is what keeps that an index-ordered lookup over the Groups still holding work, rather
        // than a scan whose cost grows with the processed messages kept for the retention period.
        // A consumer that remaps the ProcessedAt column in its own configuration has to replace this index.
        builder.HasIndex(nameof(IMessage.GroupKey), nameof(IMessage.TransactionId), nameof(IMessage.Id))
            .HasFilter($"\"{MessageColumns.ProcessedAt}\" IS NULL");
    }
}
