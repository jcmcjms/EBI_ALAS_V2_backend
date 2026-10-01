using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Notifications;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(1000);
        builder.Property(e => e.Link)
            .HasMaxLength(500);
        builder.Property(e => e.IsRead)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(e => e.CreatedAt)
            .IsRequired();
        builder.Property(e => e.Type)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(NotificationTypes.System);
        builder.Property(e => e.ReadAt);
        builder.HasIndex(e => new { e.UserId, e.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Notifications_UserId_CreatedAt");
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
