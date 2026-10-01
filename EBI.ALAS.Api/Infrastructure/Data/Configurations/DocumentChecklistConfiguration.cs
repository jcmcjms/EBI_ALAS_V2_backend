using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class DocumentChecklistConfiguration : IEntityTypeConfiguration<DocumentChecklist>
{
    public void Configure(EntityTypeBuilder<DocumentChecklist> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Pending");
        builder.HasIndex(e => new { e.LoanApplicationId, e.Code })
            .IsUnique()
            .HasDatabaseName("IX_DocumentChecklists_Loan_Code");
        builder.HasIndex(e => new { e.LoanApplicationId, e.Status })
            .HasDatabaseName("IX_DocumentChecklists_Loan_Status");
        builder.HasIndex(e => new { e.LoanApplicationId, e.Status, e.Code })
            .HasDatabaseName("IX_DocumentChecklists_Loan_Status_Code");
        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.DocumentChecklists)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.UpdatedBy)
            .WithMany()
            .HasForeignKey(e => e.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
