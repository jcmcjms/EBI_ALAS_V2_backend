using EBI.ALAS.Api.Features.ApprovalMatrix;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Junction table for Branch-scope approvers who cover multiple
// branches. Composite PK (UserId, BranchCode). Cascade on both
// sides — deleting a user or a branch cleans up the mapping.
public class UserBranchCoverageConfiguration : IEntityTypeConfiguration<UserBranchCoverage>
{
    public void Configure(EntityTypeBuilder<UserBranchCoverage> builder)
    {
        builder.HasKey(ubc => new { ubc.UserId, ubc.BranchCode });

        builder.HasIndex(ubc => ubc.BranchCode)
            .HasDatabaseName("IX_UserBranchCoverages_BranchCode");

        builder.HasOne(ubc => ubc.User)
            .WithMany(u => u.BranchCoverages)
            .HasForeignKey(ubc => ubc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ubc => ubc.Branch)
            .WithMany()
            .HasForeignKey(ubc => ubc.BranchCode)
            .HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
