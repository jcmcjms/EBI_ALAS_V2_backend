using EBI.ALAS.Api.Features.WebLoans;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Infrastructure.Data;
public class WebLoanDbContext : DbContext
{
    public WebLoanDbContext(DbContextOptions<WebLoanDbContext> options) : base(options) { }

    // ─── DbSets ──────────────────────────────────────────────────────────────
    public DbSet<CisInfo> CisInfos => Set<CisInfo>();
    public DbSet<CisInfoMiscData> CisInfoMiscDatas => Set<CisInfoMiscData>();
    public DbSet<LoanAcctInfo> LoanAcctInfos => Set<LoanAcctInfo>();
    public DbSet<LoanData> LoanDatas => Set<LoanData>();
    public DbSet<LoanStatusLookup> LoanStatuses => Set<LoanStatusLookup>();
    public DbSet<LoanProductLookup> LoanProducts => Set<LoanProductLookup>();
    public DbSet<MisGroup> MisGroups => Set<MisGroup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ─── cis_info ────────────────────────────────────────────────────
        modelBuilder.Entity<CisInfo>(entity =>
        {
            entity.HasKey(e => e.CisNo);
            entity.Property(e => e.CisNo).HasColumnName("cis_no").HasMaxLength(10);
        });

        // ─── cis_info_misc_data ──────────────────────────────────────────
        // Composite key (cis_no, id_code) — one row per attribute per client.
        modelBuilder.Entity<CisInfoMiscData>(entity =>
        {
            entity.HasKey(e => new { e.CisNo, e.IdCode });
            entity.HasIndex(e => e.CisNo);
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

        // ─── mis_group ───────────────────────────────────────────────────
        // Single-table multi-group lookup. frp_id is the synthetic PK in webloan.
        // Indexed on (group_no, path) — every ALAS lookup filters by group_no and
        // joins on path (or id_code for cis_info_misc_data).
        modelBuilder.Entity<MisGroup>(entity =>
        {
            entity.HasKey(e => e.FrpId);
            entity.HasIndex(e => new { e.GroupNo, e.Path });
            entity.HasIndex(e => new { e.GroupNo, e.IdCode });
        });
    }
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
