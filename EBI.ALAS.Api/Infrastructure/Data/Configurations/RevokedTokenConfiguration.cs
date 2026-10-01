using EBI.ALAS.Api.Features.Auth;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class RevokedTokenConfiguration : IEntityTypeConfiguration<RevokedToken>
{
    public void Configure(EntityTypeBuilder<RevokedToken> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.TokenId)
            .IsRequired()
            .HasMaxLength(200);
        builder.HasIndex(e => e.TokenId)
            .IsUnique();
        builder.Property(e => e.ExpiresAt)
            .IsRequired();
        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_RevokedTokens_ExpiresAt");
    }
}
