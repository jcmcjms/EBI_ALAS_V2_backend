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

        // Covering index for the hourly bulk delete
        // (`DELETE FROM RevokedTokens WHERE ExpiresAt < @now`).
        // Without this, every cleanup tick would table-scan the
        // entire RevokedTokens table — defeating the purpose of
        // running the cleanup at all once the table grows past
        // a few thousand rows. SQL Server can use a single
        // nonclustered seek on ExpiresAt to find the rows to
        // delete and the clustered index on Id for the deletes.
        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_RevokedTokens_ExpiresAt");
    }
}
