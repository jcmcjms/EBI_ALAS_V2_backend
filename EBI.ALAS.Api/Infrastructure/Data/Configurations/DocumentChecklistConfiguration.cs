using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Per-item document requirement status. Tracks Missing/Pending/
// Submitted/Verified lifecycle for each checklist requirement on
// a loan application.
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

        // Composite unique: one row per (loan, checklist code).
        builder.HasIndex(e => new { e.LoanApplicationId, e.Code })
            .IsUnique()
            .HasDatabaseName("IX_DocumentChecklists_Loan_Code");

        // Query pattern: get unresolved items for a loan.
        builder.HasIndex(e => new { e.LoanApplicationId, e.Status })
            .HasDatabaseName("IX_DocumentChecklists_Loan_Status");

        // Document verification: (LoanId, Status, Code) covers MarkSubmittedAsync/MarkVerifiedAsync
        builder.HasIndex(e => new { e.LoanApplicationId, e.Status, e.Code })
            .HasDatabaseName("IX_DocumentChecklists_Loan_Status_Code");

        // FK to LoanApplication. Cascade — deleting a loan cleans its checklist rows.
        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.DocumentChecklists)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to User (last updater). SetNull — deleting a user clears attribution.
        builder.HasOne(e => e.UpdatedBy)
            .WithMany()
            .HasForeignKey(e => e.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
