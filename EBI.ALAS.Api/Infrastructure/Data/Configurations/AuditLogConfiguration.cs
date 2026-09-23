using EBI.ALAS.Api.Features.AuditLogs;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Critical performance indexes for the audit log list query
        // Descending index on Timestamp ensures latest entries are returned first without a sort
        builder.HasIndex(e => e.Timestamp).IsDescending();
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Action);
        builder.HasIndex(e => e.EntityType);

        builder.Property(e => e.UserName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Action)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.EntityType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.EntityId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.EntityLabel)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Summary)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.RawChanges)
            .HasColumnType("nvarchar(max)");

        builder.Property(e => e.IpAddress)
            .HasMaxLength(50);

        builder.Property(e => e.UserAgent)
            .HasMaxLength(500);

        // Foreign key to User (optional — system events have no user)
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
