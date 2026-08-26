using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Notifications.Domain.Entities;

namespace OrderFlow.Notifications.Infrastructure.Persistence.Configurations;

public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessages");

        builder.HasKey(p => p.EventId);

        builder.Property(p => p.EventId)
            .ValueGeneratedNever();

        builder.Property(p => p.EventType)
            .IsRequired(false)
            .HasMaxLength(150)
            .HasColumnType("varchar(150)");

        builder.Property(p => p.ProcessedAt)
            .IsRequired();
    }
}
