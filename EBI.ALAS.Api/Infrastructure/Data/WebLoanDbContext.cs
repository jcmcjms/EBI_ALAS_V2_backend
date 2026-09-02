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
    public DbSet<PreLoanData> PreLoanDatas => Set<PreLoanData>();
    // amort_data — per-loan amortization schedule rows. Joined from
    // loan_data on (bk, bch, acct_no, loan_no) with `amort_no = 1` to
    // surface the first scheduled installment amount. The
    // outstanding-loans endpoint is the only consumer for now.
    public DbSet<AmortData> AmortDatas => Set<AmortData>();
    // OutstandingLoanRow — keyless projection entity carrying loan_data
    // columns + the CASE-computed `computed_amort_amount`. Materialized
    // by GetOutstandingLoansAsync. See OutstandingLoanRow.cs for the
    // rationale (EF rejects derived columns on real-table entities).
    public DbSet<OutstandingLoanRow> OutstandingLoanRows => Set<OutstandingLoanRow>();
    public DbSet<LoanStatusLookup> LoanStatuses => Set<LoanStatusLookup>();
    // loan_product — the existing LoanProductLookup entity, mapped to
    // dbo.loan_product. Reused for the pending-loan join so no separate
    // entity is needed (one entity per table is the EF rule).
    public DbSet<LoanProductLookup> LoanProducts => Set<LoanProductLookup>();
    public DbSet<LoanPurpose> LoanPurposes => Set<LoanPurpose>();
    public DbSet<CheckListData> CheckListDatas => Set<CheckListData>();
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

        // ─── amort_data ──────────────────────────────────────────────────
        // Keyless: webloan PK is (bk, bch, acct_no, loan_no, amort_no) — we
        // never fetch by it directly. The outstanding-loans query joins to
        // it from loan_data on (bk, bch, acct_no, loan_no) and filters
        // amort_no = 1, so we index the JOIN+filter columns.
        modelBuilder.Entity<AmortData>(entity =>
        {
            entity.HasNoKey();
            entity.HasIndex(e => new { e.BranchCode, e.AccountNo, e.LoanNo });
        });

        // ─── OutstandingLoanRow ─────────────────────────────────────────
        // Keyless projection shape for the outstanding-loans raw SQL.
        // Never maps to a real table — the SELECT-list alias columns are
        // bound via [Column] attributes on the entity properties. EF's
        // materializer populates them positionally from the raw query.
        modelBuilder.Entity<OutstandingLoanRow>(entity =>
        {
            entity.HasNoKey();
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

        // ─── pre_loan_data ───────────────────────────────────────────────
        // Keyless: same reasoning as loan_data — transactional table keyed
        // by (bch, acct_no, loan_no), not a single-column PK in webloan.
        modelBuilder.Entity<PreLoanData>(entity =>
        {
            entity.HasNoKey();
            entity.HasIndex(e => new { e.BranchCode, AccountNo = e.AccountNo });
        });

        // ─── loan_purpose ───────────────────────────────────────────────
        // Joined on path in the pending-loan query.
        modelBuilder.Entity<LoanPurpose>(entity =>
        {
            entity.HasKey(e => e.Path);
        });

        // ─── check_list_data ────────────────────────────────────────────
        // EAV-style attribute store. Composite key (cis_no, check_list_item).
        // Indexed on cis_no because every per-CIS enrichment in this
        // service filters by cis_no + a single item code.
        modelBuilder.Entity<CheckListData>(entity =>
        {
            entity.HasKey(e => new { e.CisNo, e.CheckListItem });
            entity.HasIndex(e => e.CisNo);
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
