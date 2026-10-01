using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
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
        builder.HasOne(e => e.Product)
            .WithMany()
            .HasForeignKey(e => e.LoanProduct)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
