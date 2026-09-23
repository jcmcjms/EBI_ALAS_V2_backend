using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

public class LoanDeviationConfiguration : IEntityTypeConfiguration<LoanDeviation>
{
    public void Configure(EntityTypeBuilder<LoanDeviation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.HasIndex(x => new { x.LoanApplicationId, x.SortOrder });

        builder.HasOne(x => x.LoanApplication).WithMany(x => x.Deviations)
            .HasForeignKey(x => x.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.ReasonText).HasMaxLength(500).IsRequired();
        builder.Property(x => x.EncoderJustification).HasMaxLength(4000);
    }
}
