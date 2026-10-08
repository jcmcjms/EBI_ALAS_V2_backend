using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebi.Alas.Api.Features.LoanApplications.Persistence;

public sealed class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
{
    public void Configure(EntityTypeBuilder<LoanApplication> builder)
    {
        builder.ToTable("LoanApplications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LamId).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.LamId).IsUnique();
        builder.Property(x => x.ApplicationGroupNo).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ClientName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BranchId).HasMaxLength(16).IsRequired();
        builder.Property(x => x.LoanType).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Principal).HasPrecision(18, 2);
        builder.Property(x => x.InterestRate).HasPrecision(9, 4);
        builder.Property(x => x.TotalInterest).HasPrecision(18, 2);
        builder.Property(x => x.TotalDeductions).HasPrecision(18, 2);
        builder.Property(x => x.NetProceeds).HasPrecision(18, 2);
    }
}
