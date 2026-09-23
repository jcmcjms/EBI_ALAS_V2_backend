using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// §5
public class IncomingLoanConfiguration : IEntityTypeConfiguration<IncomingLoan>
{
    public void Configure(EntityTypeBuilder<IncomingLoan> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Deductions).HasColumnType("decimal(18,2)");
        builder.Property(e => e.Remarks).IsRequired().HasMaxLength(500);

        builder.HasIndex(e => e.LoanApplicationId)
            .HasDatabaseName("IX_IncomingLoans_LoanApplicationId");

        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.IncomingLoans)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
