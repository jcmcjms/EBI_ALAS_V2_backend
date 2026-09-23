using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// §5
public class EbiReloanConfiguration : IEntityTypeConfiguration<EbiReloan>
{
    public void Configure(EntityTypeBuilder<EbiReloan> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Pn).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);

        builder.Property(e => e.ExistingDeduction).HasColumnType("decimal(18,2)");
        builder.Property(e => e.OutstandingBalance).HasColumnType("decimal(18,2)");
        builder.Property(e => e.PayToClose).HasColumnType("decimal(18,2)");

        builder.HasIndex(e => e.LoanApplicationId)
            .HasDatabaseName("IX_EbiReloans_LoanApplicationId");

        builder.HasOne(e => e.LoanApplication)
            .WithMany(l => l.EbiReloans)
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
