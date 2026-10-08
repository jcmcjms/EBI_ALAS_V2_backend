using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.Persistence;

public sealed class WebLoanDbContext(DbContextOptions<WebLoanDbContext> options) : DbContext(options)
{
    public DbSet<WebLoanCis> CisInfos => Set<WebLoanCis>();

    public DbSet<WebLoanProduct> LoanProducts => Set<WebLoanProduct>();

    public DbSet<WebLoanAccount> LoanAccounts => Set<WebLoanAccount>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebLoanCis>(b =>
        {
            b.HasNoKey();
            b.ToView(null);
            b.Property(x => x.CifNo).HasMaxLength(32);
            b.Property(x => x.FullName).HasMaxLength(200);
            b.Property(x => x.Address).HasMaxLength(500);
        });

        modelBuilder.Entity<WebLoanProduct>(b =>
        {
            b.HasNoKey();
            b.ToView(null);
            b.Property(x => x.ProductCode).HasMaxLength(32);
            b.Property(x => x.ProductName).HasMaxLength(200);
        });

        modelBuilder.Entity<WebLoanAccount>(b =>
        {
            b.HasNoKey();
            b.ToView(null);
            b.Property(x => x.AccountNo).HasMaxLength(32);
            b.Property(x => x.Status).HasMaxLength(32);
            b.Property(x => x.Balance).HasPrecision(18, 2);
        });

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new InvalidOperationException("WebLoan database is read-only.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("WebLoan database is read-only.");
}

public sealed class WebLoanCis
{
    public string CifNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Address { get; set; }
}

public sealed class WebLoanProduct
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal InterestRatePerMonth { get; set; }

    public bool IsActive { get; set; }
}

public sealed class WebLoanAccount
{
    public string AccountNo { get; set; } = string.Empty;

    public string CifNo { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal Balance { get; set; }
}
