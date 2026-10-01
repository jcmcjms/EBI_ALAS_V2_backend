using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class LoanProductConfiguration : IEntityTypeConfiguration<LoanProduct>
{
    public void Configure(EntityTypeBuilder<LoanProduct> builder)
    {
        builder.HasKey(e => e.Code);
        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(e => e.MinAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");
        builder.Property(e => e.MaxAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");
        builder.Property(e => e.MinTermDays).IsRequired();
        builder.Property(e => e.MaxTermDays).IsRequired();
        builder.Property(e => e.NotarialFee)
            .IsRequired()
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);
        builder.Property(e => e.DocStampFee)
            .IsRequired()
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);
        builder.Property(e => e.InsuranceFee)
            .IsRequired()
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);
        builder.Property(e => e.AdvanceInterestRate)
            .IsRequired()
            .HasColumnType("decimal(9,6)")
            .HasDefaultValue(0m);
        builder.Property(e => e.IsRetired)
            .IsRequired()
            .HasDefaultValue(true);
        builder.Property(e => e.LastSyncedAt).IsRequired();
        builder.Property(e => e.UpdatedDate)
            .IsRequired();
        builder.Property(e => e.UpdatedById);
        builder.HasIndex(e => e.UpdatedById)
            .HasDatabaseName("IX_LoanProducts_UpdatedById");
        builder.HasOne(e => e.UpdatedBy)
            .WithMany()
            .HasForeignKey(e => e.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
