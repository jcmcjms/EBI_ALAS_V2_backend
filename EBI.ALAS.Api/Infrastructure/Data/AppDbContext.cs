using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.SystemSettings;
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
    public DbSet<EbiReloan> EbiReloans => Set<EbiReloan>();
    public DbSet<IncomingLoan> IncomingLoans => Set<IncomingLoan>();
    public DbSet<LoanSubmissionIdempotency> LoanSubmissionIdempotencies => Set<LoanSubmissionIdempotency>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LoanProduct> LoanProducts => Set<LoanProduct>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<LoanDeviation> LoanDeviations => Set<LoanDeviation>();
    public DbSet<DeviationRemark> DeviationRemarks => Set<DeviationRemark>();
    public DbSet<DocumentRemark> DocumentRemarks => Set<DocumentRemark>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<LoanProductChecklist> LoanProductChecklists => Set<LoanProductChecklist>();
    public DbSet<ApprovalAuthority> ApprovalAuthorities => Set<ApprovalAuthority>();
    public DbSet<DeviationCatalogItem> DeviationCatalog => Set<DeviationCatalogItem>();

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

            // JobTitle is a free-text role label (e.g. "Senior Credit
            // Analyst"). It complements the workflow `Role` field which
            // only carries the broad category (Encoder/Recommender/etc.).
            entity.Property(e => e.JobTitle)
                .HasMaxLength(100);

            // ESignature is a base64-encoded PNG drawn from the signature
            // pad. The 2MB cap matches the FluentValidation rule in
            // UserValidators.cs so the column won't silently truncate a
            // payload the validator accepted.
            entity.Property(e => e.ESignature)
                .HasMaxLength(2000000);
        });

        // ─── LoanApplication Entity ──────────────────────────────────────
        modelBuilder.Entity<LoanApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.LamId)
                .IsRequired()
                .HasMaxLength(30);

            entity.HasIndex(e => e.LamId)
                .IsUnique();

            // ── Multi-loan submission: group key, per-loan PN/product ──
            entity.Property(e => e.ApplicationGroupNo)
                .IsRequired()
                .HasMaxLength(30);
            entity.HasIndex(e => e.ApplicationGroupNo)
                .HasDatabaseName("IX_LoanApplications_ApplicationGroupNo");
            entity.HasIndex(e => e.LoanNo)
                .HasDatabaseName("IX_LoanApplications_LoanNo");

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

            // ── §1.2 branch & type ──
            entity.Property(e => e.CreationTypeLabel).HasMaxLength(50);
            entity.Property(e => e.RequestingOfficer).HasMaxLength(150);
            entity.Property(e => e.Lai).HasMaxLength(30);

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

            entity.Property(e => e.Suffix).HasMaxLength(10);

            entity.Property(e => e.Birthdate);

            entity.Property(e => e.Address).HasMaxLength(500);

            entity.Property(e => e.Agency)
                .HasMaxLength(100);

            entity.Property(e => e.Position)
                .HasMaxLength(100);

            entity.Property(e => e.EmployeeId)
                .HasMaxLength(50);

            entity.Property(e => e.NetTakeHomePay)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.LengthOfService).HasMaxLength(50);
            entity.Property(e => e.Region).HasMaxLength(10);
            entity.Property(e => e.DivisionCode).HasMaxLength(10);
            entity.Property(e => e.StationCode).HasMaxLength(10);
            entity.Property(e => e.MisAgency).HasMaxLength(200);

            // Manual-entry information (School / Referrer) — length caps
            // mirror the Zod schema and FluentValidation on the create path.
            entity.Property(e => e.School)
                .HasMaxLength(200);

            entity.Property(e => e.Referrer)
                .HasMaxLength(100);

            // ── §3 per-loan parameters ──
            entity.Property(e => e.LoanNo)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.ProductCode)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Product)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Purpose)
                .HasMaxLength(500);

            entity.Property(e => e.ProposedAmount)
                .IsRequired()
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.TermDays)
                .IsRequired();

            // FIX: decimal(5,2) rounded 0.0966 → 0.10 silently on save.
            // Match LoanProduct.AdvanceInterestRate (decimal(9,6)).
            entity.Property(e => e.InterestRate)
                .IsRequired()
                .HasColumnType("decimal(9,6)");

            entity.Property(e => e.NthpDate);

            // ── §3 bank fees (AO entry + policy snapshot) ──
            entity.Property(e => e.NotarialFee).HasColumnType("decimal(18,2)");
            entity.Property(e => e.DocStamps).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Insurance).HasColumnType("decimal(18,2)");
            entity.Property(e => e.StandardNotarialFee).HasColumnType("decimal(18,2)");
            entity.Property(e => e.StandardDocStamps).HasColumnType("decimal(18,2)");
            entity.Property(e => e.StandardInsurance).HasColumnType("decimal(18,2)");

            // ── §6 / §7 verification + deviations ──
            entity.Property(e => e.VerificationFindings).HasMaxLength(2000);

            entity.Property(e => e.HasDeviations).IsRequired().HasDefaultValue(false);

            // DeviationDetails: List<string> JSON column with value-comparer
            // so EF can detect add/remove without a roundtrip.
            var listComparer = new ValueComparer<List<string>>(
                (c1, c2) => c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            entity.Property(e => e.DeviationDetails)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnType("nvarchar(max)")
                .Metadata.SetValueComparer(listComparer);

            // DeviationJustifications: Dictionary<string, string> JSON column.
            var mapComparer = new ValueComparer<Dictionary<string, string>>(
                (c1, c2) => c1.Count == c2.Count && !c1.Except(c2).Any(),
                c => c.Aggregate(0, (a, kv) => HashCode.Combine(a, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
                c => new Dictionary<string, string>(c));

            entity.Property(e => e.DeviationJustifications)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
                .HasColumnType("nvarchar(max)")
                .Metadata.SetValueComparer(mapComparer);

            entity.Property(e => e.Remarks).HasMaxLength(1000);
            entity.Property(e => e.AoRecommendation).HasMaxLength(1000);
            entity.Property(e => e.OtherRemarks).HasMaxLength(1000);
            entity.Property(e => e.FeeDeviationJustification).HasMaxLength(1000);

            entity.Property(e => e.PreLoanId);
            entity.Property(e => e.PreLoanFormNumber).HasMaxLength(50);

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

        // ─── OutstandingLoan Entity (resized to §4 obligations shape) ──
        modelBuilder.Entity<OutstandingLoan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Pn).IsRequired().HasMaxLength(50);

            entity.Property(e => e.PrincipalBalance).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Amortization).HasColumnType("decimal(18,2)");
            entity.Property(e => e.OutstandingBalance).HasColumnType("decimal(18,2)");

            entity.Property(e => e.DateGranted);
            entity.Property(e => e.DateMaturity);

            entity.Property(e => e.Status).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ProductWithDescription).HasMaxLength(200);

            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.OutstandingLoans)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── BuyOut Entity (resized to §5 shape) ─────────────────────────
        modelBuilder.Entity<BuyOut>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Pn).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);

            entity.Property(e => e.Amortization).HasColumnType("decimal(18,2)");
            entity.Property(e => e.OutstandingBalance).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.BuyOuts)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── EbiReloan Entity (§5) ───────────────────────────────────────
        modelBuilder.Entity<EbiReloan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Pn).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);

            entity.Property(e => e.ExistingDeduction).HasColumnType("decimal(18,2)");
            entity.Property(e => e.OutstandingBalance).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PayToClose).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.EbiReloans)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── IncomingLoan Entity (§5) ────────────────────────────────────
        modelBuilder.Entity<IncomingLoan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Deductions).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Remarks).IsRequired().HasMaxLength(500);

            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.IncomingLoans)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── LoanSubmissionIdempotency Entity ────────────────────────────
        // Replay guard for POST /api/loans: same (IdempotencyKey, UserId) ⇒
        // stored response, never a second application group.
        modelBuilder.Entity<LoanSubmissionIdempotency>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.IdempotencyKey).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.ResponseJson).IsRequired().HasColumnType("nvarchar(max)");
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => new { e.IdempotencyKey, e.UserId })
                .IsUnique()
                .HasDatabaseName("IX_LoanSubmissionIdempotency_Key_User");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
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

            // Covering index for the hourly bulk delete
            // (`DELETE FROM RevokedTokens WHERE ExpiresAt < @now`).
            // Without this, every cleanup tick would table-scan the
            // entire RevokedTokens table — defeating the purpose of
            // running the cleanup at all once the table grows past
            // a few thousand rows. SQL Server can use a single
            // nonclustered seek on ExpiresAt to find the rows to
            // delete and the clustered index on Id for the deletes.
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_RevokedTokens_ExpiresAt");
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

            // Covering index for the hourly bulk delete
            // (`DELETE FROM RefreshTokens WHERE ExpiresAt < @now OR AbsoluteExpiry < @now`).
            // Without this index, the cleanup is a full table scan on
            // a table that grows by one row per login. SQL Server uses
            // a seek on the smallest predicate (ExpiresAt) and a
            // residual filter on AbsoluteExpiry; AbsoluteExpiry is
            // strictly greater than ExpiresAt so the residual filter
            // never trips, but SQL still has to check it. A composite
            // (ExpiresAt, AbsoluteExpiry) is not necessary — the
            // single-column index is selective enough on its own.
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_RefreshTokens_ExpiresAt");
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

        // ─── Notification Entity ─────────────────────────────────────
        // Per-user inbox surfaced by the SPA header bell. The poll query
        // is "WHERE UserId = ? ORDER BY CreatedAt DESC LIMIT N" — covered
        // by the composite index below so even a 100k-row inbox stays
        // sub-millisecond.
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            // Required + indexed because every poll query filters on it.
            entity.Property(e => e.UserId).IsRequired();

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Description)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(e => e.Link)
                .HasMaxLength(500);

            entity.Property(e => e.IsRead)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            // Composite (UserId ASC, CreatedAt DESC) — the bell poll
            // filters + sorts on this pair, so the index serves both
            // clauses and eliminates the sort step entirely.
            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_Notifications_UserId_CreatedAt");

            // FK to User. Restrict (not Cascade) — deleting a user must
            // never silently wipe their in-app inbox. The audit log uses
            // SetNull because of its nullable FK; here the FK is
            // non-nullable and the inbox would be lost, so we surface it
            // as a FK violation that the admin must resolve explicitly.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
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

            entity.Property(e => e.MinTermDays).IsRequired();
            entity.Property(e => e.MaxTermDays).IsRequired();

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

            // ── Audit (UpdatedDate / UpdatedById) ────────────────────
            // UpdatedDate is datetime2 to match every other timestamp
            // column in the schema (LoanApplication.LastActionDate,
            // User.CreatedAt, AuditLog.Timestamp). Required because
            // the repository sets it on every successful
            // SaveChangesAsync — the application, not the DB, owns the
            // value, so no SQL DEFAULT.
            entity.Property(e => e.UpdatedDate)
                .IsRequired();

            // UpdatedById is a nullable int FK to User.Id. Nullable
            // because the background sync service inserts/updates rows
            // with no human attribution. On admin-driven updates the
            // endpoint layer passes the caller's user id so the column
            // is non-null at that point.
            entity.Property(e => e.UpdatedById);

            // Index on UpdatedById so the admin "show rows modified by
            // user X" filter pattern (and any future per-modifier
            // audit query) does a single seek. Single-column index —
            // a composite is unnecessary because UpdatedById is
            // already selective on its own (each user only touches a
            // handful of products), and EF can combine the index with
            // the PK seek if the predicate also narrows on Code.
            entity.HasIndex(e => e.UpdatedById)
                .HasDatabaseName("IX_LoanProducts_UpdatedById");

            // FK to User. Restrict (not Cascade) mirrors
            // LoanApplication.CreatedById — deleting a user must
            // never silently rewrite the audit history by nulling the
            // modifier on every row they touched. SetNull was the
            // alternative but it would erase attribution; an admin
            // who wants to retire a user with historical edits has to
            // first reassign or soft-delete the affected rows.
            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── LoanDeviation Entity ────────────────────────────────────
        modelBuilder.Entity<LoanDeviation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();

            e.HasIndex(x => new { x.LoanApplicationId, x.SortOrder });

            e.HasOne(x => x.LoanApplication).WithMany(x => x.Deviations)
                .HasForeignKey(x => x.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(x => x.ReasonText).HasMaxLength(500).IsRequired();
            e.Property(x => x.EncoderJustification).HasMaxLength(4000);
        });

        // ─── DeviationRemark Entity ──────────────────────────────────
        modelBuilder.Entity<DeviationRemark>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();

            e.HasIndex(x => new { x.LoanDeviationId, x.CreatedAt });

            e.HasOne(x => x.Deviation)
                .WithMany(x => x.Remarks)
                .HasForeignKey(x => x.LoanDeviationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.ParentRemark)
                .WithMany(x => x.Replies)
                .HasForeignKey(x => x.ParentRemarkId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.AuthorRole).HasMaxLength(50).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        });

        // ─── DocumentRemark Entity ──────────────────────────────────
        // Keyed by (LoanApplicationId, ChecklistIdCode) — the stable
        // checklist requirement code, NOT the binary docId. DocId is
        // snapshotted at write time for audit ("which version was reviewed").
        modelBuilder.Entity<DocumentRemark>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();

            e.HasIndex(x => new { x.LoanApplicationId, x.ChecklistIdCode, x.CreatedAt });

            e.HasOne(x => x.LoanApplication)
                .WithMany()
                .HasForeignKey(x => x.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.ParentRemark)
                .WithMany(x => x.Replies)
                .HasForeignKey(x => x.ParentRemarkId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.ChecklistIdCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.AuthorRole).HasMaxLength(50).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        });

        // ─── SystemSetting Entity ──────────────────────────────────────
        // Generic key/value store for system-wide settings (workflow flags,
        // thresholds…). PK is the string key; Value is stored as string so
        // the table never needs schema changes for new settings.
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Key);

            entity.Property(e => e.Key)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Value)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.UpdatedAt)
                .IsRequired();

            entity.Property(e => e.UpdatedById)
                .IsRequired();

            // FK to User. Restrict (not Cascade) — deleting a user must
            // never silently erase the audit trail of who changed a
            // system setting.
            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── LoanProductChecklist Entity ─────────────────────────────
        // Junction table mapping required checklist documents to each
        // loan product. Composite PK (LoanProduct, IdCode) — each row
        // says "product X requires checklist item Y".
        modelBuilder.Entity<LoanProductChecklist>(entity =>
        {
            entity.HasKey(e => new { e.LoanProduct, e.IdCode });

            entity.Property(e => e.LoanProduct)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.IdCode)
                .IsRequired()
                .HasMaxLength(20);

            // FK to LoanProduct — a checklist entry must reference a
            // valid product. Cascade so deleting a product removes its
            // checklist requirements (the product is retired, not the
            // checklist definition).
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.LoanProduct)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ApprovalAuthority Entity ───────────────────────────────
        // Delegation-of-authority matrix. Seeded from the bank's
        // approval matrix; read-only at runtime (no CRUD endpoints).
        modelBuilder.Entity<ApprovalAuthority>(entity =>
        {
            entity.HasKey(e => e.Key);

            entity.Property(e => e.Key)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.DisplayName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.MaxTotalExposure)
                .IsRequired()
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Tier)
                .IsRequired();

            entity.Property(e => e.Priority)
                .IsRequired();

            entity.Property(e => e.AllowNew)
                .IsRequired();

            entity.Property(e => e.AllowRenewal)
                .IsRequired();

            entity.Property(e => e.MaxSeverity)
                .IsRequired();

            entity.Property(e => e.ScopeType)
                .IsRequired();
        });

        // ─── DeviationCatalogItem Entity ─────────────────────────────
        // Seeded deviation severity catalog. Read-only at runtime.
        modelBuilder.Entity<DeviationCatalogItem>(entity =>
        {
            entity.HasKey(e => e.Description);

            entity.Property(e => e.Description)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.Severity)
                .IsRequired();
        });

        // ─── Update Branch: add AreaCode ────────────────────────────
        modelBuilder.Entity<Branch>(entity =>
        {
            entity.Property(e => e.AreaCode)
                .HasMaxLength(10);
        });

        // ─── Update User: add ApprovalAuthorityKey ─────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.ApprovalAuthorityKey)
                .HasMaxLength(50);

            entity.HasIndex(e => e.ApprovalAuthorityKey)
                .HasDatabaseName("IX_Users_ApprovalAuthorityKey");
        });

        // ─── Update LoanApplication: add routing fields ─────────────
        modelBuilder.Entity<LoanApplication>(entity =>
        {
            entity.Property(e => e.LoanType)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("New");

            entity.Property(e => e.DeviationSeverity)
                .IsRequired()
                .HasDefaultValue(DeviationSeverity.None);

            entity.Property(e => e.RequiredApprovalTier);

            entity.Property(e => e.AssignedApproverId);

            // Navigation property — EF emits a LEFT JOIN so list projections
            // can resolve AssignedApproverName without an N+1 subquery.
            entity.HasOne(e => e.AssignedApprover)
                .WithMany()
                .HasForeignKey(e => e.AssignedApproverId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.AssignedAt);

            entity.Property(e => e.DocumentsCompleteAt);

            // Index for the assignment query pattern
            entity.HasIndex(e => new { e.Status, e.AssignedApproverId })
                .HasDatabaseName("IX_LoanApplications_Status_AssignedApprover");

            // Index for the routing tier query
            entity.HasIndex(e => new { e.Status, e.RequiredApprovalTier, e.AssignedApproverId })
                .HasDatabaseName("IX_LoanApplications_RoutingQueue");
        });
    }
}