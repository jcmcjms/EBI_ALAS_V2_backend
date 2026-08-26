using EBI.ALAS.Api.Features.WebLoans;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Infrastructure.Data;

/// <summary>
/// Read-only database context for the existing WebLoan system database.
/// Used to pull borrower and loan data from the legacy webloan DB on the same server.
///
/// IMPORTANT:
/// - This database already exists and is owned by the WebLoan system.
/// - NEVER run EF Core migrations against this context.
/// - SaveChanges is intentionally blocked to enforce read-only access.
/// </summary>
public class WebLoanDbContext : DbContext
{
    public WebLoanDbContext(DbContextOptions<WebLoanDbContext> options) : base(options) { }

    // ─── DbSets ──────────────────────────────────────────────────────────────
    public DbSet<CisInfo> CisInfos => Set<CisInfo>();
    public DbSet<LoanAcctInfo> LoanAcctInfos => Set<LoanAcctInfo>();
    public DbSet<LoanData> LoanDatas => Set<LoanData>();
    public DbSet<LoanStatusLookup> LoanStatuses => Set<LoanStatusLookup>();
    public DbSet<LoanProductLookup> LoanProducts => Set<LoanProductLookup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ─── cis_info ────────────────────────────────────────────────────
        modelBuilder.Entity<CisInfo>(entity =>
        {
            entity.HasKey(e => e.CisNo);
            entity.Property(e => e.CisNo).HasColumnName("cis_no").HasMaxLength(10);
        });

        // ─── loan_acct_info ──────────────────────────────────────────────
        modelBuilder.Entity<LoanAcctInfo>(entity =>
        {
            entity.HasKey(e => new { e.BankCode, e.BranchCode, e.AccountNo });
            entity.HasIndex(e => e.CisNo);
        });

        // ─── loan_data ───────────────────────────────────────────────────
        // Keyless: loan_no is nullable in webloan (ledger rows carry no PN)
        // and this context is read-only — no tracking required.
        modelBuilder.Entity<LoanData>(entity =>
        {
            entity.HasNoKey();
            entity.HasIndex(e => e.AccountNo);
        });

        // ─── lookups ─────────────────────────────────────────────────────
        modelBuilder.Entity<LoanStatusLookup>(entity =>
        {
            entity.HasKey(e => e.IdCode);
        });

        modelBuilder.Entity<LoanProductLookup>(entity =>
        {
            entity.HasKey(e => e.IdCode);
        });
    }

    /// <summary>
    /// WebLoan DB is read-only from this API. Any write attempt fails fast
    /// with a clear message instead of a confusing SQL permission error.
    /// </summary>
    public override int SaveChanges()
    {
        ThrowReadOnly();
        return 0;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowReadOnly();
        return Task.FromResult(0);
    }

    private static void ThrowReadOnly() =>
        throw new InvalidOperationException(
            "WebLoanDbContext is READ-ONLY. The webloan database is owned by the WebLoan system; " +
            "this API may only query it.");
}
