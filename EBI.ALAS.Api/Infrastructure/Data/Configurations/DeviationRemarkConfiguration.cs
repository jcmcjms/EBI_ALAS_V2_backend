using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

public class DeviationRemarkConfiguration : IEntityTypeConfiguration<DeviationRemark>
{
    public void Configure(EntityTypeBuilder<DeviationRemark> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.HasIndex(x => new { x.LoanDeviationId, x.CreatedAt });

        builder.HasOne(x => x.Deviation)
            .WithMany(x => x.Remarks)
            .HasForeignKey(x => x.LoanDeviationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ParentRemark)
            .WithMany(x => x.Replies)
            .HasForeignKey(x => x.ParentRemarkId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Author)
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.AuthorRole).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
    }
}
