using Alas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alas.Infrastructure.Loans;

public class LoanEntityConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("Loans", "loan");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.LoanNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.BorrowerName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(l => l.BorrowerContact)
            .HasMaxLength(200);

        builder.Property(l => l.PrincipalAmount)
            .HasPrecision(18, 2);

        builder.Property(l => l.InterestRate)
            .HasPrecision(5, 2);

        builder.Property(l => l.Purpose)
            .HasMaxLength(1000);

        builder.Property(l => l.BranchId)
            .HasMaxLength(100);

        builder.Property(l => l.Remarks)
            .HasMaxLength(2000);

        builder.Property(l => l.RejectionReason)
            .HasMaxLength(2000);

        builder.HasIndex(l => l.LoanNumber)
            .IsUnique();

        builder.HasIndex(l => l.Status);

        builder.HasIndex(l => l.BranchId);

        builder.HasIndex(l => l.CreatedUtc);

        builder.HasIndex(l => l.BorrowerName);
    }
}
