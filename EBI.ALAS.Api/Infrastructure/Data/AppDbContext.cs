using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace EBI.ALAS.Api.Infrastructure.Data;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ─── DbSets ──────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();
    public DbSet<LoanAction> LoanActions => Set<LoanAction>();
    public DbSet<OutstandingLoan> OutstandingLoans => Set<OutstandingLoan>();
    public DbSet<BuyOut> BuyOuts => Set<BuyOut>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LoanProduct> LoanProducts => Set<LoanProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ─── Branch Entity ─────────────────────────────────────────────────
        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Code)
                .IsRequired()
                .HasMaxLength(20);

            entity.HasIndex(e => e.Code)
                .IsUnique();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.CreatedAt)
                .IsRequired();
        });

        // ─── User Entity ─────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasIndex(e => e.Username)
                .IsUnique();

            entity.Property(e => e.PasswordHash)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.MiddleName)
                .HasMaxLength(100);

            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.BranchId)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Role)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.MustChangePassword)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.Email)
                .HasMaxLength(100);

            entity.Property(e => e.Phone)
                .HasMaxLength(20);

            entity.Property(e => e.EmergencyContact)
                .HasMaxLength(200);

            entity.Property(e => e.ProfilePhotoUrl)
                .HasMaxLength(500);

            entity.Property(e => e.PasswordChangedAt);
        });

        // ─── LoanApplication Entity ──────────────────────────────────────
        modelBuilder.Entity<LoanApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.FormNumber)
                .IsRequired()
                .HasMaxLength(30);

            entity.HasIndex(e => e.FormNumber)
                .IsUnique();

            // Composite indexes for common query patterns
            entity.HasIndex(e => new { e.Status, e.BranchCode })
                .HasDatabaseName("IX_LoanApplications_Status_BranchCode");

            entity.HasIndex(e => new { e.Status, e.BranchCode, e.ApplicationDate })
                .HasDatabaseName("IX_LoanApplications_Status_BranchCode_Date")
                .IsDescending(false, false, true);

            entity.HasIndex(e => new { e.CreatedById, e.Status })
                .HasDatabaseName("IX_LoanApplications_CreatedById_Status");

            entity.Property(e => e.BranchCode)
                .IsRequired()
                .HasMaxLength(20);

            // Client Information
            entity.Property(e => e.CisId)
                .HasMaxLength(50);

            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.MiddleName)
                .HasMaxLength(100);

            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Agency)
                .HasMaxLength(100);

            entity.Property(e => e.Position)
                .HasMaxLength(100);

            entity.Property(e => e.EmployeeId)
                .HasMaxLength(50);

            entity.Property(e => e.NetTakeHomePay)
                .HasColumnType("decimal(18,2)");

            // Manual-entry information (School / Referrer) — length caps
            // mirror the Zod schema and FluentValidation on the create path.
            entity.Property(e => e.School)
                .HasMaxLength(200);

            entity.Property(e => e.Referrer)
                .HasMaxLength(100);

            // Loan Parameters
            entity.Property(e => e.Product)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Purpose)
                .HasMaxLength(500);

            entity.Property(e => e.ProposedAmount)
                .IsRequired()
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.TermMonths)
                .IsRequired();

            entity.Property(e => e.InterestRate)
                .IsRequired()
                .HasColumnType("decimal(5,2)");

            entity.Property(e => e.ModeOfPayment)
                .HasMaxLength(50);

            entity.Property(e => e.CoMaker)
                .HasMaxLength(200);

            // Status & Audit
            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Draft");

            entity.Property(e => e.ApplicationDate)
                .IsRequired();

            entity.Property(e => e.LastActionDate)
                .IsRequired();

            entity.Property(e => e.CreatedById)
                .IsRequired();

            // WebLoan Traceability
            entity.Property(e => e.WebLoanCisNo)
                .HasMaxLength(50);

            entity.Property(e => e.WebLoanBranchCode)
                .HasMaxLength(20);

            var listComparer = new ValueComparer<List<string>>(
                (c1, c2) => c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            entity.Property(e => e.WebLoanAccountNumbers)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnType("nvarchar(max)")
                .Metadata.SetValueComparer(listComparer);

            entity.Property(e => e.WebLoanPnNumbers)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnType("nvarchar(max)")
                .Metadata.SetValueComparer(listComparer);

            entity.Property(e => e.WebLoanLastSyncedAt);

            // Foreign Key to User
            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── LoanAction Entity ───────────────────────────────────────────
        modelBuilder.Entity<LoanAction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.FromStatus)
                .HasMaxLength(50);

            entity.Property(e => e.ToStatus)
                .HasMaxLength(50);

            entity.Property(e => e.Comments)
                .HasMaxLength(1000);

            entity.Property(e => e.ActionDate)
                .IsRequired();

            // Foreign Keys
            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.Actions)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ActionByUser)
                .WithMany()
                .HasForeignKey(e => e.ActionByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── OutstandingLoan Entity ──────────────────────────────────────
        modelBuilder.Entity<OutstandingLoan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.CreditorName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.MonthlyPayment)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Balance)
                .HasColumnType("decimal(18,2)");

            // Foreign Key
            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.OutstandingLoans)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── BuyOut Entity ───────────────────────────────────────────────
        modelBuilder.Entity<BuyOut>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.CreditorName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Amount)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.MonthlyAmortization)
                .HasColumnType("decimal(18,2)");

            // Foreign Key
            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.BuyOuts)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── RevokedToken Entity ─────────────────────────────────────────
        modelBuilder.Entity<RevokedToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.TokenId)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasIndex(e => e.TokenId)
                .IsUnique();

            entity.Property(e => e.ExpiresAt)
                .IsRequired();
        });

        // ─── RefreshToken Entity ────────────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.TokenHash)
                .IsRequired()
                .HasMaxLength(128); // SHA-256 hex = 64 chars, generous limit

            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            entity.Property(e => e.UserId)
                .IsRequired();

            entity.Property(e => e.DeviceInfo)
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.ExpiresAt)
                .IsRequired();

            entity.Property(e => e.AbsoluteExpiry)
                .IsRequired();

            entity.Property(e => e.IsRevoked)
                .IsRequired()
                .HasDefaultValue(false);

            // Cascade: deleting a user deletes their refresh tokens
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── AuditLog Entity ───────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            // Critical performance indexes for the audit log list query
            // Descending index on Timestamp ensures latest entries are returned first without a sort
            entity.HasIndex(e => e.Timestamp).IsDescending();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => e.EntityType);

            entity.Property(e => e.UserName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.EntityType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.EntityId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.EntityLabel)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Summary)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.RawChanges)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.IpAddress)
                .HasMaxLength(50);

            entity.Property(e => e.UserAgent)
                .HasMaxLength(500);

            // Foreign key to User (optional — system events have no user)
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── LoanProduct Entity ──────────────────────────────────────
        // ALAS-owned mirror of webloan.loan_product. The PK is the
        // webloan id_code (string) so the sync upsert is idempotent by
        // natural key. IsRetired is derived from webloan's
        // `expiration IS NOT NULL` at sync time — no second source of
        // truth, no manual toggle.
        modelBuilder.Entity<LoanProduct>(entity =>
        {
            entity.HasKey(e => e.Code);
            entity.Property(e => e.Code)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Description)
                .IsRequired()
                .HasMaxLength(200);

            // Eligibility bounds — column types chosen to match the
            // existing decimal conventions in LoanApplication so the
            // disbursement math is homogeneous.
            entity.Property(e => e.MinAmount)
                .IsRequired()
                .HasColumnType("decimal(18,2)");
            entity.Property(e => e.MaxAmount)
                .IsRequired()
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.MinTermMonths).IsRequired();
            entity.Property(e => e.MaxTermMonths).IsRequired();

            // Fees — all PHP, all decimal(18,2) for consistency with
            // proposed-amount.
            entity.Property(e => e.NotarialFee)
                .IsRequired()
                .HasColumnType("decimal(18,2)")
                .HasDefaultValue(0m);
            entity.Property(e => e.DocStampFee)
                .IsRequired()
                .HasColumnType("decimal(18,2)")
                .HasDefaultValue(0m);
            entity.Property(e => e.InsuranceFee)
                .IsRequired()
                .HasColumnType("decimal(18,2)")
                .HasDefaultValue(0m);

            // decimal(9,6) supports up to 999.999999% — comfortably
            // above the realistic 0.05–0.30 range. 6 fractional digits
            // is enough precision for a per-annum rate; over-precise
            // values would imply false accuracy to ops.
            entity.Property(e => e.AdvanceInterestRate)
                .IsRequired()
                .HasColumnType("decimal(9,6)")
                .HasDefaultValue(0m);

            // Sync state. IsRetired defaults to true so a brand-new
            // mirror row inserted with default policy values is hidden
            // from the dropdown until the first successful sync run
            // proves it actually exists in webloan AND has a
            // non-retired state. Prevents "phantom" products from
            // appearing in encoders' dropdowns if the sync has never
            // run.
            entity.Property(e => e.IsRetired)
                .IsRequired()
                .HasDefaultValue(true);
            entity.Property(e => e.LastSyncedAt).IsRequired();
        });
    }
}
