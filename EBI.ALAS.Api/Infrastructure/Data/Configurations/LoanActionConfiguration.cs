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
        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.Actions)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.ActionByUser)
            .WithMany()
            .HasForeignKey(e => e.ActionByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.LoanApplicationId)
            .HasDatabaseName("IX_LoanActions_LoanId");
        builder.HasIndex(e => new { e.LoanApplicationId, e.ActionDate })
            .HasDatabaseName("IX_LoanActions_Loan_ApplicationDate");
        builder.HasIndex(e => new { e.ToStatus, e.ActionDate })
            .HasDatabaseName("IX_LoanActions_ToStatus_ActionDate");
        builder.HasIndex(e => new { e.ActionByUserId, e.ActionDate })
            .HasDatabaseName("IX_LoanActions_ActionBy_ActionDate");
    }
}
