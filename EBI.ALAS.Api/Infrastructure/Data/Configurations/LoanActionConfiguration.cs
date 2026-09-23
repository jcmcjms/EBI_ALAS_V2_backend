using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

public class LoanActionConfiguration : IEntityTypeConfiguration<LoanAction>
{
    public void Configure(EntityTypeBuilder<LoanAction> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Action)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.FromStatus)
            .HasMaxLength(50);

        builder.Property(e => e.ToStatus)
            .HasMaxLength(50);

        builder.Property(e => e.Comments)
            .HasMaxLength(1000);

        builder.Property(e => e.ActionDate)
            .IsRequired();

        // Foreign Keys
        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.Actions)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ActionByUser)
            .WithMany()
            .HasForeignKey(e => e.ActionByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Performance indexes for LoanAction. The append-only audit trail
        // had ZERO indexes in the EF model — not even an FK index on LoanApplicationId.
        // Every fresh environment (CI, new dev, DR, load-test) would table-scan
        // the hottest queries in the system.

        // FK index — required for the loan list's latest-action OUTER APPLY probe
        builder.HasIndex(e => e.LoanApplicationId)
            .HasDatabaseName("IX_LoanActions_LoanId");

        // Latest action per loan (OUTER APPLY in GetLoans)
        builder.HasIndex(e => new { e.LoanApplicationId, e.ActionDate })
            .HasDatabaseName("IX_LoanActions_Loan_ApplicationDate");

        // Dashboard: weekActions (group by ToStatus for 7-day counts)
        builder.HasIndex(e => new { e.ToStatus, e.ActionDate })
            .HasDatabaseName("IX_LoanActions_ToStatus_ActionDate");

        // Dashboard: activeRows / "now serving" (who is currently acting)
        builder.HasIndex(e => new { e.ActionByUserId, e.ActionDate })
            .HasDatabaseName("IX_LoanActions_ActionBy_ActionDate");
    }
}
