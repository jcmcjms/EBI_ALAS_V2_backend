using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
{
    public void Configure(EntityTypeBuilder<LoanApplication> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.LamId)
            .IsRequired()
            .HasMaxLength(30);
        builder.HasIndex(e => e.LamId)
            .IsUnique();
        builder.Property(e => e.ApplicationGroupNo)
            .IsRequired()
            .HasMaxLength(30);
        builder.HasIndex(e => e.ApplicationGroupNo)
            .HasDatabaseName("IX_LoanApplications_ApplicationGroupNo");
        builder.HasIndex(e => e.LoanNo)
            .HasDatabaseName("IX_LoanApplications_LoanNo");
        builder.HasIndex(e => new { e.Status, e.BranchCode })
            .HasDatabaseName("IX_LoanApplications_Status_BranchCode");
        builder.HasIndex(e => new { e.Status, e.BranchCode, e.ApplicationDate })
            .HasDatabaseName("IX_LoanApplications_Status_BranchCode_Date")
            .IsDescending(false, false, true);
        builder.HasIndex(e => new { e.CreatedById, e.Status })
            .HasDatabaseName("IX_LoanApplications_CreatedById_Status");
        builder.HasIndex(e => new { e.Status, e.LastActionDate })
            .HasDatabaseName("IX_LoanApplications_Status_LastAction")
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.BranchCode, e.Status, e.ApplicationDate })
            .HasDatabaseName("IX_LoanApplications_BranchCode_Status_ApplicationDate")
            .IsDescending(false, false, true);
        builder.HasIndex(e => new { e.BranchCode, e.ApplicationDate })
            .HasDatabaseName("IX_LoanApplications_Branch_ApplicationDate")
            .IsDescending(false, true);
        builder.Property(e => e.BranchCode)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.CreationTypeLabel).HasMaxLength(50);
        builder.Property(e => e.RequestingOfficer).HasMaxLength(150);
        builder.Property(e => e.Lai).HasMaxLength(30);
        builder.Property(e => e.CisId)
            .HasMaxLength(50);
        builder.Property(e => e.FirstName)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(e => e.MiddleName)
            .HasMaxLength(100);
        builder.Property(e => e.LastName)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(e => e.Suffix).HasMaxLength(10);
        builder.Property(e => e.Birthdate);
        builder.Property(e => e.Address).HasMaxLength(500);
        builder.Property(e => e.Agency)
            .HasMaxLength(100);
        builder.Property(e => e.Position)
            .HasMaxLength(100);
        builder.Property(e => e.EmployeeId)
            .HasMaxLength(50);
        builder.Property(e => e.NetTakeHomePay)
            .HasColumnType("decimal(18,2)");
        builder.Property(e => e.LengthOfService).HasMaxLength(50);
        builder.Property(e => e.Region).HasMaxLength(10);
        builder.Property(e => e.DivisionCode).HasMaxLength(10);
        builder.Property(e => e.StationCode).HasMaxLength(10);
        builder.Property(e => e.MisAgency).HasMaxLength(200);
        builder.Property(e => e.School)
            .HasMaxLength(200);
        builder.Property(e => e.Referrer)
            .HasMaxLength(100);
        builder.Property(e => e.LoanNo)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(e => e.ProductCode)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.Product)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(e => e.Purpose)
            .HasMaxLength(500);
        builder.Property(e => e.ProposedAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");
        builder.Property(e => e.TermDays)
            .IsRequired();
        builder.Property(e => e.InterestRate)
            .IsRequired()
            .HasColumnType("decimal(9,6)");
        builder.Property(e => e.PolicyTermMonths);
        builder.Property(e => e.ApprovalTermDays);
        builder.Property(e => e.AnnualRatePercent)
            .HasColumnType("decimal(9,4)");
        builder.Property(e => e.CDocStamp).HasColumnType("decimal(18,2)");
        builder.Property(e => e.NthpDate);
        builder.Property(e => e.NotarialFee).HasColumnType("decimal(18,2)");
        builder.Property(e => e.DocStamps).HasColumnType("decimal(18,2)");
        builder.Property(e => e.Insurance).HasColumnType("decimal(18,2)");
        builder.Property(e => e.StandardNotarialFee).HasColumnType("decimal(18,2)");
        builder.Property(e => e.StandardDocStamps).HasColumnType("decimal(18,2)");
        builder.Property(e => e.StandardInsurance).HasColumnType("decimal(18,2)");
        builder.Property(e => e.VerificationFindings).HasMaxLength(2000);
        builder.Property(e => e.HasDeviations).IsRequired().HasDefaultValue(false);
        var listComparer = new ValueComparer<List<string>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());
        builder.Property(e => e.DeviationDetails)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(listComparer);
        var mapComparer = new ValueComparer<Dictionary<string, string>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.Count == c2.Count && !c1.Except(c2).Any()),
            c => c.Aggregate(0, (a, kv) => HashCode.Combine(a, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
            c => new Dictionary<string, string>(c));
        builder.Property(e => e.DeviationJustifications)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(mapComparer);
        builder.Property(e => e.Remarks).HasMaxLength(1000);
        builder.Property(e => e.AoRecommendation).HasMaxLength(1000);
        builder.Property(e => e.OtherRemarks).HasMaxLength(1000);
        builder.Property(e => e.FeeDeviationJustification).HasMaxLength(1000);
        builder.Property(e => e.PreLoanId);
        builder.Property(e => e.PreLoanFormNumber).HasMaxLength(50);
        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Draft");
        builder.Property(e => e.ApplicationDate)
            .IsRequired();
        builder.Property(e => e.LastActionDate)
            .IsRequired();
        builder.Property(e => e.CreatedById)
            .IsRequired();
        builder.Property(e => e.WebLoanCisNo)
            .HasMaxLength(50);
        builder.Property(e => e.WebLoanBranchCode)
            .HasMaxLength(20);
        builder.Property(e => e.WebLoanAccountNumbers)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(listComparer);
        builder.Property(e => e.WebLoanPnNumbers)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(listComparer);
        builder.Property(e => e.WebLoanLastSyncedAt);
        builder.HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.LoanType)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("New");
        builder.Property(e => e.DeviationSeverity)
            .IsRequired()
            .HasDefaultValue(DeviationSeverity.None);
        builder.Property(e => e.RequiredApprovalTier);
        builder.Property(e => e.AssignedApproverId);
        builder.HasOne(e => e.AssignedApprover)
            .WithMany()
            .HasForeignKey(e => e.AssignedApproverId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(e => e.AssignedAt);
        builder.Property(e => e.DocumentsCompleteAt);
        builder.Property(e => e.DocumentsFlaggedAt);
        builder.Property(e => e.DocumentsFlaggedById);
        builder.Property(e => e.DocumentFlagReason).HasMaxLength(2000);
        builder.HasOne(e => e.DocumentsFlaggedBy)
            .WithMany()
            .HasForeignKey(e => e.DocumentsFlaggedById)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => e.DocumentsFlaggedAt)
            .HasDatabaseName("IX_LoanApplications_DocumentsFlaggedAt")
            .HasFilter("[DocumentsFlaggedAt] IS NOT NULL");
        builder.HasIndex(e => new { e.Status, e.AssignedApproverId })
            .HasDatabaseName("IX_LoanApplications_Status_AssignedApprover");
        builder.HasIndex(e => new { e.Status, e.RequiredApprovalTier, e.AssignedApproverId })
            .HasDatabaseName("IX_LoanApplications_RoutingQueue");
        builder.HasIndex(e => new { e.CreatedById, e.ApplicationDate })
            .HasDatabaseName("IX_LoanApplications_CreatedById_ApplicationDate")
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.Status, e.AssignedApproverId, e.RequiredApprovalTier })
            .HasDatabaseName("IX_LoanApplications_Status_Assigned_Tier");
    }
}
