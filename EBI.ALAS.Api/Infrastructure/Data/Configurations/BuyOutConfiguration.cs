using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Resized to §5 shape
public class BuyOutConfiguration : IEntityTypeConfiguration<BuyOut>
{
    public void Configure(EntityTypeBuilder<BuyOut> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Pn).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);

        builder.Property(e => e.Amortization).HasColumnType("decimal(18,2)");
        builder.Property(e => e.OutstandingBalance).HasColumnType("decimal(18,2)");

        builder.HasIndex(e => e.LoanApplicationId)
            .HasDatabaseName("IX_BuyOuts_LoanApplicationId");

        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.BuyOuts)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
