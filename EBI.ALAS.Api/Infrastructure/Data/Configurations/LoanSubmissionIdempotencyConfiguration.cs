using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class LoanSubmissionIdempotencyConfiguration : IEntityTypeConfiguration<LoanSubmissionIdempotency>
{
    public void Configure(EntityTypeBuilder<LoanSubmissionIdempotency> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.IdempotencyKey).IsRequired();
        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.ResponseJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.HasIndex(e => new { e.IdempotencyKey, e.UserId })
            .IsUnique()
            .HasDatabaseName("IX_LoanSubmissionIdempotency_Key_User");
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
