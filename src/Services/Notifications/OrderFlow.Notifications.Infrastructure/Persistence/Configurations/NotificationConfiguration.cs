using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Notifications.Domain.Entities;

namespace OrderFlow.Notifications.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .ValueGeneratedNever();

        builder.Property(n => n.OrderId)
            .IsRequired();

        builder.HasIndex(n => n.OrderId)
            .HasDatabaseName("IX_Notifications_OrderId");

        builder.Property(n => n.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnType("varchar(50)");

        builder.Property(n => n.Message)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnType("varchar(500)");

        builder.Property(n => n.CreatedAt)
            .IsRequired();
    }
}
