using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnType("varchar(100)");

        builder.Property(m => m.Version)
            .IsRequired();

        builder.Property(m => m.Payload)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.Property(m => m.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.ProcessedAt)
            .IsRequired(false);

        builder.Property(m => m.LastError)
            .IsRequired(false)
            .HasColumnType("text");

        builder.Property(m => m.NextRetryAtUtc)
            .IsRequired(false);

        builder.Property(m => m.LockId)
            .IsRequired(false)
            .HasMaxLength(100)
            .HasColumnType("varchar(100)");

        builder.Property(m => m.LockedUntilUtc)
            .IsRequired(false);

        builder.HasIndex(m => new { m.ProcessedAt, m.NextRetryAtUtc, m.CreatedAt })
            .HasDatabaseName("IX_OutboxMessages_Pending");

        builder.HasIndex(m => m.LockedUntilUtc)
            .HasDatabaseName("IX_OutboxMessages_LockedUntilUtc");
    }
}
