using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.Persistence;

/// <summary>Read-only EF mapping for the external WebLoan database.</summary>
public sealed class WebLoanDbContext(DbContextOptions<WebLoanDbContext> options) : DbContext(options)
{
    public DbSet<WebLoanCisInfo> CisInfos => Set<WebLoanCisInfo>();

    public DbSet<WebLoanLoanAccount> LoanAccounts => Set<WebLoanLoanAccount>();

    public DbSet<WebLoanProduct> LoanProducts => Set<WebLoanProduct>();

    public DbSet<WebLoanCisMisc> CisMiscDatas => Set<WebLoanCisMisc>();

    public DbSet<WebLoanCheckList> CheckLists => Set<WebLoanCheckList>();

    public DbSet<WebLoanMisGroup> MisGroups => Set<WebLoanMisGroup>();

    public DbSet<WebLoanPurpose> LoanPurposes => Set<WebLoanPurpose>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebLoanCisInfo>(b =>
        {
            b.ToTable("cis_info", "dbo");
            b.HasKey(x => x.CisNo);
            b.Property(x => x.CisNo).HasColumnName("cis_no").HasMaxLength(10);
            b.Property(x => x.FirstName).HasColumnName("fname");
            b.Property(x => x.MiddleName).HasColumnName("mname");
            b.Property(x => x.LastName).HasColumnName("lname");
            b.Property(x => x.Title).HasColumnName("title");
            b.Property(x => x.Appelation).HasColumnName("appelation");
            b.Property(x => x.BirthDateRaw).HasColumnName("p_bday");
            b.Property(x => x.HouseStreet).HasColumnName("h_sadd");
            b.Property(x => x.Barangay).HasColumnName("h_barangay");
            b.Property(x => x.Village).HasColumnName("h_village");
            b.Property(x => x.City).HasColumnName("h_city");
            b.Property(x => x.StateProvince).HasColumnName("h_state_prov");
            b.Property(x => x.Zip).HasColumnName("h_zip");
            b.Property(x => x.Occupation).HasColumnName("occupation");
            b.Property(x => x.JobTitle).HasColumnName("b_jtitle");
            b.Property(x => x.RegionCode).HasColumnName("b_region_code");
            b.Property(x => x.DivisionCode).HasColumnName("b_division_code");
            b.Property(x => x.StationCode).HasColumnName("b_station_code");
            b.Property(x => x.EmployeeNo).HasColumnName("b_employee_no");
            b.Property(x => x.BankCode).HasColumnName("bk");
            b.Property(x => x.BranchCode).HasColumnName("bch");
        });

        modelBuilder.Entity<WebLoanLoanAccount>(b =>
        {
            b.ToTable("loan_acct_info", "dbo");
            b.HasKey(x => new { x.BankCode, x.BranchCode, x.AccountNo });
            b.HasIndex(x => x.CisNo);
            b.Property(x => x.BankCode).HasColumnName("bk");
            b.Property(x => x.BranchCode).HasColumnName("bch");
            b.Property(x => x.AccountNo).HasColumnName("acct_no");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.CisNo).HasColumnName("cis_no");
            b.Property(x => x.CreditLimit).HasColumnName("credit_limit").HasPrecision(18, 2);
            b.Property(x => x.UsedCredit).HasColumnName("used_credit").HasPrecision(18, 2);
            b.Property(x => x.BorrowerType).HasColumnName("borrower_type");
            b.Property(x => x.MisGroup2).HasColumnName("cat_mis_group2");
            b.Property(x => x.Solicitor).HasColumnName("solicitor");
        });

        modelBuilder.Entity<WebLoanCisMisc>(b =>
        {
            b.ToTable("cis_info_misc_data", "dbo");
            b.HasKey(x => new { x.CisNo, x.IdCode });
            b.Property(x => x.CisNo).HasColumnName("cis_no");
            b.Property(x => x.IdCode).HasColumnName("id_code");
            b.Property(x => x.ValueStr).HasColumnName("value_str");
        });

        modelBuilder.Entity<WebLoanCheckList>(b =>
        {
            b.ToTable("check_list_data", "dbo");
            b.HasKey(x => new { x.CisNo, x.CheckListItem });
            b.Property(x => x.CisNo).HasColumnName("cis_no");
            b.Property(x => x.CheckListItem).HasColumnName("check_list_item");
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.Submitted).HasColumnName("submitted");
            b.Property(x => x.Expiration).HasColumnName("expiration");
        });

        modelBuilder.Entity<WebLoanMisGroup>(b =>
        {
            b.ToTable("mis_group", "dbo");
            b.HasKey(x => x.FrpId);
            b.Property(x => x.FrpId).HasColumnName("frp_id");
            b.Property(x => x.GroupNo).HasColumnName("group_no");
            b.Property(x => x.IdCode).HasColumnName("id_code");
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.Path).HasColumnName("path");
        });

        modelBuilder.Entity<WebLoanPurpose>(b =>
        {
            b.ToTable("loan_purpose", "dbo");
            b.HasKey(x => x.Path);
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.Description).HasColumnName("description");
        });

        modelBuilder.Entity<WebLoanProduct>(b =>
        {
            b.HasNoKey();
            b.ToTable("loan_product", "dbo");
            b.Property(x => x.ProductCode).HasColumnName("id_code").HasMaxLength(32);
            b.Property(x => x.ProductName).HasColumnName("description").HasMaxLength(200);
            b.Property(x => x.Expiration).HasColumnName("expiration");
        });

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new InvalidOperationException("WebLoan database is read-only.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("WebLoan database is read-only.");
}

public sealed class WebLoanCisInfo
{
    public string CisNo { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Appelation { get; set; }

    public string? BirthDateRaw { get; set; }

    public string? HouseStreet { get; set; }

    public string? Barangay { get; set; }

    public string? Village { get; set; }

    public string? City { get; set; }

    public string? StateProvince { get; set; }

    public string? Zip { get; set; }

    public string? Occupation { get; set; }

    public string? JobTitle { get; set; }

    public string? RegionCode { get; set; }

    public string? DivisionCode { get; set; }

    public string? StationCode { get; set; }

    public string? EmployeeNo { get; set; }

    public string? BankCode { get; set; }

    public string? BranchCode { get; set; }
}

public sealed class WebLoanLoanAccount
{
    public string BankCode { get; set; } = string.Empty;

    public string BranchCode { get; set; } = string.Empty;

    public string AccountNo { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string CisNo { get; set; } = string.Empty;

    public decimal? CreditLimit { get; set; }

    public decimal? UsedCredit { get; set; }

    public string? BorrowerType { get; set; }

    public string? MisGroup2 { get; set; }

    public string? Solicitor { get; set; }
}

public sealed class WebLoanCisMisc
{
    public string CisNo { get; set; } = string.Empty;

    public int IdCode { get; set; }

    public string? ValueStr { get; set; }
}

public sealed class WebLoanCheckList
{
    public string CisNo { get; set; } = string.Empty;

    public string CheckListItem { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? Submitted { get; set; }

    public DateTime? Expiration { get; set; }
}

public sealed class WebLoanMisGroup
{
    public int FrpId { get; set; }

    public int GroupNo { get; set; }

    public string IdCode { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Path { get; set; }
}

public sealed class WebLoanPurpose
{
    public string Path { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public sealed class WebLoanProduct
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    /// <summary>webloan expiration — non-null means the catalog row is retired.</summary>
    public DateTime? Expiration { get; set; }
}
