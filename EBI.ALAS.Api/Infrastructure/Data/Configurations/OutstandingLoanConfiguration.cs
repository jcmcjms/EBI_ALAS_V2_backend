using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Resized to §4 obligations shape
public class OutstandingLoanConfiguration : IEntityTypeConfiguration<OutstandingLoan>
{
    public void Configure(EntityTypeBuilder<OutstandingLoan> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Pn).IsRequired().HasMaxLength(50);

        builder.Property(e => e.PrincipalBalance).HasColumnType("decimal(18,2)");
        builder.Property(e => e.Amortization).HasColumnType("decimal(18,2)");
        builder.Property(e => e.OutstandingBalance).HasColumnType("decimal(18,2)");

        builder.Property(e => e.DateGranted);
        builder.Property(e => e.DateMaturity);

        builder.Property(e => e.Status).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ProductWithDescription).HasMaxLength(200);

        builder.HasIndex(e => e.LoanApplicationId)
            .HasDatabaseName("IX_OutstandingLoans_LoanApplicationId");

        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.OutstandingLoans)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
