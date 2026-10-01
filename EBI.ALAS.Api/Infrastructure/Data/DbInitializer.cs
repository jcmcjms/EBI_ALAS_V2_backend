using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Infrastructure.Data;
public static class DbInitializer
{
    public static async Task InitializeAsync(AppDbContext context, IServiceProvider serviceProvider)
    {
        var timeProvider = serviceProvider.GetRequiredService<ITimeProvider>();
        var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();
        try
        {
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed during startup. The application will halt.");
            throw;
        }
        await SeedBranchesAsync(context, timeProvider);
        await SeedAdminUserAsync(context, timeProvider);
        await SeedSystemUserAsync(context, timeProvider);
        await SeedLoanProductsAsync(context, timeProvider);
        await SeedLoanProductChecklistAsync(context);
        await SeedApprovalAuthoritiesAsync(context);
        await SeedDeviationCatalogAsync(context);
        await SeedBranchAreaCodesAsync(context);
    }
    private static async Task SeedBranchesAsync(AppDbContext context, ITimeProvider timeProvider)
    {
        if (await context.Branches.AnyAsync())
            return;
        var now = timeProvider.UtcNow;
        var branches = new List<Branch>
        {
            new() { Code = "000", Name = "Lianga Branch", IsActive = true, CreatedAt = now },
            new() { Code = "002", Name = "Barobo Branch", IsActive = true, CreatedAt = now },
            new() { Code = "003", Name = "San Francisco Branch", IsActive = true, CreatedAt = now },
            new() { Code = "004", Name = "Arasasan Branch", IsActive = true, CreatedAt = now },
            new() { Code = "005", Name = "Hinatuan Branch", IsActive = true, CreatedAt = now },
            new() { Code = "006", Name = "Tagum Branch", IsActive = true, CreatedAt = now },
            new() { Code = "007", Name = "Tandag Branch", IsActive = true, CreatedAt = now },
            new() { Code = "008", Name = "Butuan Branch", IsActive = true, CreatedAt = now },
            new() { Code = "009", Name = "Bislig Branch", IsActive = true, CreatedAt = now },
            new() { Code = "011", Name = "Head Office Branch", IsActive = true, CreatedAt = now },
            new() { Code = "012", Name = "Cagayan Branch", IsActive = true, CreatedAt = now },
            new() { Code = "013", Name = "Talisay Branch", IsActive = true, CreatedAt = now },
            new() { Code = "014", Name = "General Santos Branch", IsActive = true, CreatedAt = now },
            new() { Code = "015", Name = "Panabo Branch", IsActive = true, CreatedAt = now },
            new() { Code = "016", Name = "Valencia Branch", IsActive = true, CreatedAt = now },
            new() { Code = "017", Name = "Cateel Branch", IsActive = true, CreatedAt = now },
            new() { Code = "018", Name = "Davao-Buhangin Branch", IsActive = true, CreatedAt = now },
            new() { Code = "019", Name = "Tacloban Branch", IsActive = true, CreatedAt = now },
            new() { Code = "020", Name = "Bacolod Branch", IsActive = true, CreatedAt = now },
            new() { Code = "021", Name = "Iloilo Branch", IsActive = true, CreatedAt = now },
            new() { Code = "022", Name = "Davao-Matina Branch", IsActive = true, CreatedAt = now },
            new() { Code = "023", Name = "Trento Branch", IsActive = true, CreatedAt = now },
            new() { Code = "024", Name = "Mati Branch", IsActive = true, CreatedAt = now },
            new() { Code = "025", Name = "Bayugan Branch", IsActive = true, CreatedAt = now },
            new() { Code = "026", Name = "Nabunturan Branch", IsActive = true, CreatedAt = now },
            new() { Code = "027", Name = "Madrid Branch", IsActive = true, CreatedAt = now },
            new() { Code = "028", Name = "Surigao Branch", IsActive = true, CreatedAt = now },
            new() { Code = "029", Name = "Gingoog Branch", IsActive = true, CreatedAt = now },
            new() { Code = "030", Name = "CTS (Mandaue) Branch", IsActive = true, CreatedAt = now },
            new() { Code = "031", Name = "Ronda Branch", IsActive = true, CreatedAt = now },
            new() { Code = "991", Name = "Corporate Center", IsActive = true, CreatedAt = now },
        };
        context.Branches.AddRange(branches);
        await context.SaveChangesAsync();
    }
    private static async Task SeedAdminUserAsync(AppDbContext context, ITimeProvider timeProvider)
    {
        if (await context.Users.AnyAsync(u => u.Username == "admin"))
            return;
        var adminUser = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            FirstName = "System",
            MiddleName = null,
            LastName = "Administrator",
            BranchId = "011",
            Role = "Admin",
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = timeProvider.UtcNow
        };
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
    }
    private static async Task SeedSystemUserAsync(AppDbContext context, ITimeProvider timeProvider)
    {
        if (await context.Users.AnyAsync(u => u.Username == "system"))
            return;
        var systemUser = new User
        {
            Username = "system",
            PasswordHash = "!",
            FirstName = "System",
            LastName = "Gate",
            BranchId = "011",
            Role = "Encoder",
            IsActive = false,
            CreatedAt = timeProvider.UtcNow,
        };
        context.Users.Add(systemUser);
        await context.SaveChangesAsync();
    }
    private static async Task SeedLoanProductsAsync(AppDbContext context, ITimeProvider timeProvider)
    {
        if (await context.LoanProducts.AnyAsync())
            return;
        var now = timeProvider.UtcNow;
        var products = new List<LoanProduct>
        {
            new()
            {
                Code = "A16",
                Description = "Salary Loan - Private",
                MinAmount = 5_000,
                MaxAmount = 1_500_000,
                MinTermDays = 30,
                MaxTermDays = 2_555,
                ApplicationChargeRate = 0.06m,
                AmortizationMode = "DIM",
                ChargeAdvanceInterest = false,
                IsRetired = false,
                LastSyncedAt = now,
                UpdatedDate = now,
            },
            new()
            {
                Code = "A17",
                Description = "Salary Loan - Government",
                MinAmount = 5_000,
                MaxAmount = 1_500_000,
                MinTermDays = 30,
                MaxTermDays = 2_555,
                ApplicationChargeRate = 0.06m,
                AmortizationMode = "DIM",
                ChargeAdvanceInterest = false,
                IsRetired = false,
                LastSyncedAt = now,
                UpdatedDate = now,
            },
            new()
            {
                Code = "C02",
                Description = "Consumptive Loan",
                MinAmount = 5_000,
                MaxAmount = 1_500_000,
                MinTermDays = 30,
                MaxTermDays = 2_555,
                ApplicationChargeRate = 0.06m,
                AmortizationMode = "DIM",
                ChargeAdvanceInterest = false,
                IsRetired = false,
                LastSyncedAt = now,
                UpdatedDate = now,
            },
            new()
            {
                Code = "C23",
                Description = "Consumptive Loan - Teachers",
                MinAmount = 5_000,
                MaxAmount = 1_500_000,
                MinTermDays = 30,
                MaxTermDays = 2_555,
                ApplicationChargeRate = 0.06m,
                AmortizationMode = "DIM",
                ChargeAdvanceInterest = false,
                IsRetired = false,
                LastSyncedAt = now,
                UpdatedDate = now,
            },
            new()
            {
                Code = "C35",
                Description = "Consumptive Loan - MIC",
                MinAmount = 5_000,
                MaxAmount = 1_500_000,
                MinTermDays = 30,
                MaxTermDays = 2_555,
                ApplicationChargeRate = 0.075m,
                AmortizationMode = "MIC",
                ChargeAdvanceInterest = true,
                AdvanceInterestRate = 0.18m,
                IsRetired = false,
                LastSyncedAt = now,
                UpdatedDate = now,
            },
            new()
            {
                Code = "C21",
                Description = "Consumptive Loan - Special",
                MinAmount = 5_000,
                MaxAmount = 1_500_000,
                MinTermDays = 30,
                MaxTermDays = 2_555,
                ApplicationChargeRate = 0.06m,
                AmortizationMode = "DIM",
                ChargeAdvanceInterest = false,
                IsRetired = false,
                LastSyncedAt = now,
                UpdatedDate = now,
            },
        };
        context.LoanProducts.AddRange(products);
        await context.SaveChangesAsync();
    }
    private static async Task SeedLoanProductChecklistAsync(AppDbContext context)
    {
        if (await context.LoanProductChecklists.AnyAsync())
            return;
        var checklistItems = new List<LoanProductChecklist>
        {
            new() { LoanProduct = "A16", IdCode = "A1004" },
            new() { LoanProduct = "A16", IdCode = "A2008" },
            new() { LoanProduct = "A16", IdCode = "A2016" },
            new() { LoanProduct = "A16", IdCode = "A2017" },
            new() { LoanProduct = "A16", IdCode = "A2018" },
            new() { LoanProduct = "A16", IdCode = "A2019" },
            new() { LoanProduct = "A16", IdCode = "A2020" },
            new() { LoanProduct = "A16", IdCode = "A2021" },
            new() { LoanProduct = "A16", IdCode = "A2035" },
            new() { LoanProduct = "A16", IdCode = "A4001" },
            new() { LoanProduct = "A16", IdCode = "CCR38" },
            new() { LoanProduct = "A16", IdCode = "PIC02" },
            new() { LoanProduct = "A16", IdCode = "PIC03" },
            new() { LoanProduct = "A17", IdCode = "A1004" },
            new() { LoanProduct = "A17", IdCode = "A2008" },
            new() { LoanProduct = "A17", IdCode = "A2016" },
            new() { LoanProduct = "A17", IdCode = "A2017" },
            new() { LoanProduct = "A17", IdCode = "A2018" },
            new() { LoanProduct = "A17", IdCode = "A2019" },
            new() { LoanProduct = "A17", IdCode = "A2020" },
            new() { LoanProduct = "A17", IdCode = "A2021" },
            new() { LoanProduct = "A17", IdCode = "A2022" },
            new() { LoanProduct = "A17", IdCode = "A2027" },
            new() { LoanProduct = "A17", IdCode = "A2035" },
            new() { LoanProduct = "A17", IdCode = "A4001" },
            new() { LoanProduct = "A17", IdCode = "CCR38" },
            new() { LoanProduct = "A17", IdCode = "PIC02" },
            new() { LoanProduct = "A17", IdCode = "PIC03" },
            new() { LoanProduct = "C02", IdCode = "A1004" },
            new() { LoanProduct = "C02", IdCode = "A2008" },
            new() { LoanProduct = "C02", IdCode = "A2018" },
            new() { LoanProduct = "C02", IdCode = "A2019" },
            new() { LoanProduct = "C02", IdCode = "A2027" },
            new() { LoanProduct = "C02", IdCode = "A4001" },
            new() { LoanProduct = "C02", IdCode = "CCR38" },
            new() { LoanProduct = "C02", IdCode = "PIC02" },
            new() { LoanProduct = "C02", IdCode = "PIC03" },
            new() { LoanProduct = "C23", IdCode = "A1004" },
            new() { LoanProduct = "C23", IdCode = "A2008" },
            new() { LoanProduct = "C23", IdCode = "A2018" },
            new() { LoanProduct = "C23", IdCode = "A2019" },
            new() { LoanProduct = "C23", IdCode = "A2027" },
            new() { LoanProduct = "C23", IdCode = "A4001" },
            new() { LoanProduct = "C23", IdCode = "CCR38" },
            new() { LoanProduct = "C23", IdCode = "PIC02" },
            new() { LoanProduct = "C23", IdCode = "PIC03" },
            new() { LoanProduct = "C35", IdCode = "A1004" },
            new() { LoanProduct = "C35", IdCode = "A2008" },
            new() { LoanProduct = "C35", IdCode = "A2018" },
            new() { LoanProduct = "C35", IdCode = "A2019" },
            new() { LoanProduct = "C35", IdCode = "A2027" },
            new() { LoanProduct = "C35", IdCode = "A4001" },
            new() { LoanProduct = "C35", IdCode = "CCR38" },
            new() { LoanProduct = "C35", IdCode = "PIC02" },
            new() { LoanProduct = "C35", IdCode = "PIC03" },
            new() { LoanProduct = "C21", IdCode = "A1004" },
            new() { LoanProduct = "C21", IdCode = "A2008" },
            new() { LoanProduct = "C21", IdCode = "A2018" },
            new() { LoanProduct = "C21", IdCode = "A2019" },
            new() { LoanProduct = "C21", IdCode = "A2020" },
            new() { LoanProduct = "C21", IdCode = "A2021" },
            new() { LoanProduct = "C21", IdCode = "A2022" },
            new() { LoanProduct = "C21", IdCode = "A2023" },
            new() { LoanProduct = "C21", IdCode = "A2027" },
            new() { LoanProduct = "C21", IdCode = "A4001" },
            new() { LoanProduct = "C21", IdCode = "CCR38" },
            new() { LoanProduct = "C21", IdCode = "PIC02" },
            new() { LoanProduct = "C21", IdCode = "PIC03" },
        };
        context.LoanProductChecklists.AddRange(checklistItems);
        await context.SaveChangesAsync();
    }
    private static async Task SeedApprovalAuthoritiesAsync(AppDbContext context)
    {
        var ceoRow = await context.ApprovalAuthorities.FindAsync("CEO");
        var presidentRow = await context.ApprovalAuthorities.FindAsync("President");
        if (ceoRow is not null && presidentRow is not null)
        {
            var presidentUsers = await context.Users
                .Where(u => u.ApprovalAuthorityKey == "President")
                .ToListAsync();
            foreach (var u in presidentUsers)
            {
                u.ApprovalAuthorityKey = "CEOPresident";
                u.JobTitle = "CEO / President";
            }
            context.ApprovalAuthorities.Remove(ceoRow);
            context.ApprovalAuthorities.Remove(presidentRow);
            context.ApprovalAuthorities.Add(new ApprovalAuthority
            {
                Key = "CEOPresident",
                DisplayName = "CEO / President",
                Tier = 5,
                Priority = 1,
                AllowNew = true,
                AllowRenewal = true,
                MaxSeverity = DeviationSeverity.Major,
                MaxTotalExposure = 1_500_000,
                ScopeType = AuthorityScope.Global,
            });
            await context.SaveChangesAsync();
        }
        if (await context.ApprovalAuthorities.AnyAsync())
            return;
        var authorities = new List<ApprovalAuthority>
        {
            new() { Key = "BranchHead", DisplayName = "Branch Head", Tier = 1, Priority = 1,
                AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,
                MaxTotalExposure = 300_000, ScopeType = AuthorityScope.Branch },
            new() { Key = "OICLevel1", DisplayName = "OIC Level 1", Tier = 1, Priority = 2,
                AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,
                MaxTotalExposure = 300_000, ScopeType = AuthorityScope.Branch },
            new() { Key = "AreaHead", DisplayName = "Area Head", Tier = 2, Priority = 1,
                AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,
                MaxTotalExposure = 600_000, ScopeType = AuthorityScope.Area },
            new() { Key = "RBGHead", DisplayName = "RBG Head", Tier = 3, Priority = 1,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,
                MaxTotalExposure = 1_000_000, ScopeType = AuthorityScope.Global },
            new() { Key = "ProductHead", DisplayName = "Product Head", Tier = 3, Priority = 2,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,
                MaxTotalExposure = 1_000_000, ScopeType = AuthorityScope.Global },
            new() { Key = "CreditHead", DisplayName = "Credit Head", Tier = 3, Priority = 3,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,
                MaxTotalExposure = 1_000_000, ScopeType = AuthorityScope.Global },
            new() { Key = "COO", DisplayName = "Chief Operating Officer", Tier = 4, Priority = 1,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,
                MaxTotalExposure = 1_200_000, ScopeType = AuthorityScope.Global },
            new() { Key = "CEOPresident", DisplayName = "CEO / President", Tier = 5, Priority = 1,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,
                MaxTotalExposure = 1_500_000, ScopeType = AuthorityScope.Global },
            new() { Key = "CreComChair", DisplayName = "CreCom Chair Level D", Tier = 5, Priority = 2,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,
                MaxTotalExposure = 1_500_000, ScopeType = AuthorityScope.Global },
        };
        context.ApprovalAuthorities.AddRange(authorities);
        await context.SaveChangesAsync();
    }
    private static async Task SeedDeviationCatalogAsync(AppDbContext context)
    {
        if (await context.DeviationCatalog.AnyAsync())
            return;
        var catalog = new List<DeviationCatalogItem>
        {
            new() { Id = 1,  Description = "Age not within the prescribed parameters", Severity = DeviationSeverity.Major },
            new() { Id = 2,  Description = "Discounted Application Fee", Severity = DeviationSeverity.Major },
            new() { Id = 3,  Description = "Interest rate reduction", Severity = DeviationSeverity.Major },
            new() { Id = 4,  Description = "With past due account - non performing loan", Severity = DeviationSeverity.Major },
            new() { Id = 5,  Description = "Lacking bank statement of account", Severity = DeviationSeverity.Minor },
            new() { Id = 6,  Description = "Lacking CIBI", Severity = DeviationSeverity.Minor },
            new() { Id = 7,  Description = "Lacking marriage cert. with surname as single", Severity = DeviationSeverity.Minor },
            new() { Id = 8,  Description = "Lacking one or two payslip(s) for new atm loan", Severity = DeviationSeverity.Minor },
            new() { Id = 9,  Description = "Lacking signature in application form", Severity = DeviationSeverity.Minor },
            new() { Id = 10, Description = "Lacking SPAs to claim ATM", Severity = DeviationSeverity.Minor },
            new() { Id = 11, Description = "No appointment record and/or service record", Severity = DeviationSeverity.Minor },
            new() { Id = 12, Description = "No FI SOA and loan ledger", Severity = DeviationSeverity.Minor },
            new() { Id = 13, Description = "No latest payslip", Severity = DeviationSeverity.Minor },
            new() { Id = 14, Description = "No interview sheet", Severity = DeviationSeverity.Minor },
            new() { Id = 15, Description = "No orientation form or old form submitted", Severity = DeviationSeverity.Minor },
            new() { Id = 16, Description = "No valid identification cards", Severity = DeviationSeverity.Minor },
            new() { Id = 17, Description = "Total consumer loan exposure exceeding 1.2 million", Severity = DeviationSeverity.Minor },
            new() { Id = 18, Description = "With blocked ATIM in same school", Severity = DeviationSeverity.Minor },
            new() { Id = 19, Description = "With history of delinquency in the latest loan availment", Severity = DeviationSeverity.Minor },
            new() { Id = 20, Description = "With NFIS findings", Severity = DeviationSeverity.Minor },
            new() { Id = 21, Description = "With past due account - performing", Severity = DeviationSeverity.Minor },
        };
        context.DeviationCatalog.AddRange(catalog);
        await context.SaveChangesAsync();
    }
    private static async Task SeedBranchAreaCodesAsync(AppDbContext context)
    {
        var areaMapping = new Dictionary<string, string>
        {
            ["003"] = "A1", ["008"] = "A1", ["012"] = "A1", ["023"] = "A1",
            ["025"] = "A1", ["026"] = "A1", ["028"] = "A1", ["029"] = "A1",
            ["000"] = "A2", ["002"] = "A2", ["004"] = "A2", ["005"] = "A2",
            ["007"] = "A2", ["009"] = "A2", ["017"] = "A2", ["027"] = "A2",
            ["006"] = "A3", ["011"] = "A3", ["014"] = "A3", ["015"] = "A3",
            ["016"] = "A3", ["022"] = "A3", ["024"] = "A3",
            ["013"] = "A4", ["019"] = "A4", ["020"] = "A4", ["021"] = "A4",
            ["030"] = "A4", ["031"] = "A4",
        };
        var branches = await context.Branches.ToListAsync();
        foreach (var branch in branches)
        {
            if (areaMapping.TryGetValue(branch.Code, out var areaCode))
            {
                branch.AreaCode = areaCode;
            }
        }
        await context.SaveChangesAsync();
    }
}
