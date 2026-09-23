using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Notifications;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Per-user inbox surfaced by the SPA header bell. The poll query
// is "WHERE UserId = ? ORDER BY CreatedAt DESC LIMIT N" — covered
// by the composite index below so even a 100k-row inbox stays
// sub-millisecond.
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Required + indexed because every poll query filters on it.
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

        // Notification type bucket — defaults to "system" for existing rows
        // and callers that don't specify a type. MaxLength 20 matches the
        // longest constant ("application" = 11 chars, headroom for future).
        builder.Property(e => e.Type)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(NotificationTypes.System);

        // Timestamp of when the owner marked it read. Null = unread.
        builder.Property(e => e.ReadAt);

        // Composite (UserId ASC, CreatedAt DESC) — the bell poll
        // filters + sorts on this pair, so the index serves both
        // clauses and eliminates the sort step entirely.
        builder.HasIndex(e => new { e.UserId, e.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Notifications_UserId_CreatedAt");

        // FK to User. Restrict (not Cascade) — deleting a user must
        // never silently wipe their in-app inbox. The audit log uses
        // SetNull because of its nullable FK; here the FK is
        // non-nullable and the inbox would be lost, so we surface it
        // as a FK violation that the admin must resolve explicitly.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
