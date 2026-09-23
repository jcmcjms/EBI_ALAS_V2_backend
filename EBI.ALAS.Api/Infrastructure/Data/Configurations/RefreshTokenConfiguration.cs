using EBI.ALAS.Api.Features.Auth;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.TokenHash)
            .IsRequired()
            .HasMaxLength(128); // SHA-256 hex = 64 chars, generous limit

        builder.HasIndex(e => e.TokenHash)
            .IsUnique();

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.DeviceInfo)
            .HasMaxLength(500);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.ExpiresAt)
            .IsRequired();

        builder.Property(e => e.AbsoluteExpiry)
            .IsRequired();

        builder.Property(e => e.IsRevoked)
            .IsRequired()
            .HasDefaultValue(false);

        // Cascade: deleting a user deletes their refresh tokens
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Covering index for the hourly bulk delete
        // (`DELETE FROM RefreshTokens WHERE ExpiresAt < @now OR AbsoluteExpiry < @now`).
        // Without this index, the cleanup is a full table scan on
        // a table that grows by one row per login. SQL Server uses
        // a seek on the smallest predicate (ExpiresAt) and a
        // residual filter on AbsoluteExpiry; AbsoluteExpiry is
        // strictly greater than ExpiresAt so the residual filter
        // never trips, but SQL still has to check it. A composite
        // (ExpiresAt, AbsoluteExpiry) is not necessary — the
        // single-column index is selective enough on its own.
        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_RefreshTokens_ExpiresAt");
    }
}
