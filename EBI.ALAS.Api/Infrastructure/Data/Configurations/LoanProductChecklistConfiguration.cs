using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Junction table mapping required checklist documents to each
// loan product. Composite PK (LoanProduct, IdCode) — each row
// says "product X requires checklist item Y".
public class LoanProductChecklistConfiguration : IEntityTypeConfiguration<LoanProductChecklist>
{
    public void Configure(EntityTypeBuilder<LoanProductChecklist> builder)
    {
        builder.HasKey(e => new { e.LoanProduct, e.IdCode });

        builder.Property(e => e.LoanProduct)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.IdCode)
            .IsRequired()
            .HasMaxLength(20);

        // FK to LoanProduct — a checklist entry must reference a
        // valid product. Cascade so deleting a product removes its
        // checklist requirements (the product is retired, not the
        // checklist definition).
        builder.HasOne(e => e.Product)
            .WithMany()
            .HasForeignKey(e => e.LoanProduct)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
