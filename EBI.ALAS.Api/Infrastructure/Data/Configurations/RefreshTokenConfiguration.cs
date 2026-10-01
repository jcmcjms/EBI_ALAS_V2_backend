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
            .HasMaxLength(128);
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
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_RefreshTokens_ExpiresAt");
    }
}
