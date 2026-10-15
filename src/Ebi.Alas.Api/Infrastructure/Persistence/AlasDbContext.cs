using Ebi.Alas.Api.Features.Auth.Domain;
using Ebi.Alas.Api.Features.Auth.Persistence;
using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.Branches.Persistence;
using Ebi.Alas.Api.Features.Deviations;
using Ebi.Alas.Api.Features.Documents;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Persistence;
using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Presence;
using Ebi.Alas.Api.Features.Revisions;
using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.Persistence;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Features.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.Persistence;

public sealed class AlasDbContext(DbContextOptions<AlasDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();

    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();

    public DbSet<WorkflowQueueItem> WorkflowQueueItems => Set<WorkflowQueueItem>();

    public DbSet<DocumentChecklistItem> DocumentChecklistItems => Set<DocumentChecklistItem>();

    public DbSet<DocumentRemark> DocumentRemarks => Set<DocumentRemark>();

    public DbSet<LoanApplicationAction> LoanApplicationActions => Set<LoanApplicationAction>();

    public DbSet<LoanDeviation> LoanDeviations => Set<LoanDeviation>();

    public DbSet<RevisionRequest> RevisionRequests => Set<RevisionRequest>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<SignatureChainEntry> SignatureChainEntries => Set<SignatureChainEntry>();

    public DbSet<LoanProduct> LoanProducts => Set<LoanProduct>();

    public DbSet<LoanProductDocumentRequirement> LoanProductDocumentRequirements => Set<LoanProductDocumentRequirement>();

    public DbSet<UserPresence> UserPresences => Set<UserPresence>();

    public DbSet<Branch> Branches => Set<Branch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new RevokedTokenConfiguration());
        modelBuilder.ApplyConfiguration(new LoanApplicationConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowQueueItemConfiguration());
        modelBuilder.ApplyConfiguration(new BranchConfiguration());

        modelBuilder.Entity<SystemSetting>(b =>
        {
            b.ToTable("SystemSettings");
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasMaxLength(100).IsRequired();
            b.Property(x => x.Value).HasMaxLength(500).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.HasOne(x => x.UpdatedBy)
                .WithMany()
                .HasForeignKey(x => x.UpdatedById)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.UpdatedById);
        });

        modelBuilder.Entity<DocumentChecklistItem>(b =>
        {
            b.ToTable("DocumentChecklistItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(x => x.LoanApplicationId);
        });

        modelBuilder.Entity<DocumentRemark>(b =>
        {
            b.ToTable("DocumentRemarks");
            b.HasKey(x => x.Id);
            b.Property(x => x.ChecklistIdCode).HasMaxLength(50).IsRequired();
            b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => x.LoanApplicationId);
            b.HasIndex(x => x.ParentRemarkId);
        });

        modelBuilder.Entity<LoanApplicationAction>(b =>
        {
            b.ToTable("LoanApplicationActions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(50).IsRequired();
            b.Property(x => x.FromStatus).HasMaxLength(50);
            b.Property(x => x.ToStatus).HasMaxLength(50);
            b.Property(x => x.Comments).HasMaxLength(2000);
            b.HasIndex(x => x.LoanApplicationId);
        });

        modelBuilder.Entity<LoanProductDocumentRequirement>(b =>
        {
            b.ToTable("LoanProductDocumentRequirements");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProductCode).HasMaxLength(20).IsRequired();
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.Property(x => x.Description).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.ProductCode, x.Code }).IsUnique();
        });

        modelBuilder.Entity<LoanDeviation>(b =>
        {
            b.ToTable("LoanDeviations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.LoanApplicationId);
        });

        modelBuilder.Entity<RevisionRequest>(b =>
        {
            b.ToTable("RevisionRequests");
            b.HasKey(x => x.Id);
            b.Property(x => x.Section).HasMaxLength(100).IsRequired();
            b.Property(x => x.Comment).HasMaxLength(1000).IsRequired();
            b.HasIndex(x => x.LoanApplicationId);
        });

        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("Notifications");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Type).HasConversion<int>();
            b.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<SignatureChainEntry>(b =>
        {
            b.ToTable("SignatureChainEntries");
            b.HasKey(x => x.Id);
            b.Property(x => x.RoleName).HasMaxLength(64).IsRequired();
            b.Property(x => x.Status).HasConversion<int>();
            b.HasIndex(x => x.LoanApplicationId);
        });

        modelBuilder.Entity<LoanProduct>(b =>
        {
            b.ToTable("LoanProducts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.InterestRatePerMonth).HasPrecision(9, 4);
            b.Property(x => x.MinAmount).HasPrecision(18, 2);
            b.Property(x => x.MaxAmount).HasPrecision(18, 2);
            b.Property(x => x.NotarialFee).HasPrecision(18, 2);
            b.Property(x => x.DocStampFee).HasPrecision(18, 2);
            b.Property(x => x.InsuranceFee).HasPrecision(18, 2);
            b.Property(x => x.AdvanceInterestRate).HasPrecision(9, 6);
            b.Property(x => x.ApplicationChargeRate).HasPrecision(9, 6);
            b.Property(x => x.AmortizationMode).HasMaxLength(8).IsRequired();
        });

        modelBuilder.Entity<UserPresence>(b =>
        {
            b.ToTable("UserPresences");
            b.HasKey(x => x.UserId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
