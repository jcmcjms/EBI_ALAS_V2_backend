using EBI.ALAS.Api.Features.WebLoans;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Infrastructure.Data;
public class WebLoanDbContext : DbContext
{
    public WebLoanDbContext(DbContextOptions<WebLoanDbContext> options) : base(options) { }
    public DbSet<CisInfo> CisInfos => Set<CisInfo>();
    public DbSet<CisInfoMiscData> CisInfoMiscDatas => Set<CisInfoMiscData>();
    public DbSet<LoanAcctInfo> LoanAcctInfos => Set<LoanAcctInfo>();
    public DbSet<LoanData> LoanDatas => Set<LoanData>();
    public DbSet<PreLoanData> PreLoanDatas => Set<PreLoanData>();
    public DbSet<AmortData> AmortDatas => Set<AmortData>();
    public DbSet<OutstandingLoanRow> OutstandingLoanRows => Set<OutstandingLoanRow>();
    public DbSet<PendingLoanRow> PendingLoanRows => Set<PendingLoanRow>();
    public DbSet<LoanStatusLookup> LoanStatuses => Set<LoanStatusLookup>();
    public DbSet<LoanProductLookup> LoanProducts => Set<LoanProductLookup>();
    public DbSet<LoanPurpose> LoanPurposes => Set<LoanPurpose>();
    public DbSet<CheckListData> CheckListDatas => Set<CheckListData>();
    public DbSet<MisGroup> MisGroups => Set<MisGroup>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CisInfo>(entity =>
        {
            entity.HasKey(e => e.CisNo);
            entity.Property(e => e.CisNo).HasColumnName("cis_no").HasMaxLength(10);
        });
        modelBuilder.Entity<CisInfoMiscData>(entity =>
        {
            entity.HasKey(e => new { e.CisNo, e.IdCode });
            entity.HasIndex(e => e.CisNo);
            entity.Property(e => e.CisNo).HasColumnName("cis_no").HasMaxLength(10);
        });
        modelBuilder.Entity<LoanAcctInfo>(entity =>
        {
            entity.HasKey(e => new { e.BankCode, e.BranchCode, e.AccountNo });
            entity.HasIndex(e => e.CisNo);
        });
        modelBuilder.Entity<LoanData>(entity =>
        {
            entity.HasNoKey();
            entity.HasIndex(e => e.AccountNo);
        });
        modelBuilder.Entity<AmortData>(entity =>
        {
            entity.HasNoKey();
            entity.HasIndex(e => new { e.BranchCode, e.AccountNo, e.LoanNo });
        });
        modelBuilder.Entity<OutstandingLoanRow>(entity =>
        {
            entity.HasNoKey();
        });
        modelBuilder.Entity<PendingLoanRow>(entity =>
        {
            entity.HasNoKey();
        });
        modelBuilder.Entity<LoanStatusLookup>(entity =>
        {
            entity.HasKey(e => e.IdCode);
        });
        modelBuilder.Entity<LoanProductLookup>(entity =>
        {
            entity.HasKey(e => e.IdCode);
        });
        modelBuilder.Entity<PreLoanData>(entity =>
        {
            entity.HasNoKey();
            entity.HasIndex(e => new { e.BranchCode, AccountNo = e.AccountNo });
        });
        modelBuilder.Entity<LoanPurpose>(entity =>
        {
            entity.HasKey(e => e.Path);
        });
        modelBuilder.Entity<CheckListData>(entity =>
        {
            entity.HasKey(e => new { e.CisNo, e.CheckListItem });
            entity.HasIndex(e => e.CisNo);
        });
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
