using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Keyed by (LoanApplicationId, ChecklistIdCode) — the stable
// checklist requirement code, NOT the binary docId. DocId is
// snapshotted at write time for audit ("which version was reviewed").
public class DocumentRemarkConfiguration : IEntityTypeConfiguration<DocumentRemark>
{
    public void Configure(EntityTypeBuilder<DocumentRemark> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.HasIndex(x => new { x.LoanApplicationId, x.ChecklistIdCode, x.CreatedAt });

        builder.HasOne(x => x.LoanApplication)
            .WithMany()
            .HasForeignKey(x => x.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ParentRemark)
            .WithMany(x => x.Replies)
            .HasForeignKey(x => x.ParentRemarkId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Author)
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.ChecklistIdCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AuthorRole).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
    }
}
