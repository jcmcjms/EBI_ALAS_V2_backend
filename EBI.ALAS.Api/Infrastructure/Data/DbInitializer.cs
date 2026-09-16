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
        // Get time provider for consistent timestamp generation
        var timeProvider = serviceProvider.GetRequiredService<ITimeProvider>();
        var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();

        // NOTE: We intentionally do NOT call EnsureCreatedAsync here.
        // That method is incompatible with Migrations: if any migration
        // has already created the schema, EnsureCreatedAsync becomes a
        // no-op, and any pending migration is silently ignored. We use
        // MigrateAsync instead, which walks the __EFMigrationsHistory
        // table and applies whatever's missing.
        //
        // If you have a fresh dev environment and want the schema
        // bootstrapped without authoring migrations yet, run:
        //   dotnet ef database update
        // from the project directory — this generates the
        // __EFMigrationsHistory row automatically.
        //
        // Any failure here (missing connection, auth error, pending
        // migration that conflicts) is logged loudly and rethrown so
        // the app fails fast rather than silently booting with a
        // half-migrated schema.
        try
        {
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed during startup. The application will halt.");
            throw;
        }

        // Check if database already has data
        if (await context.Users.AnyAsync())
        {
            return; // Database already seeded
        }

        // Seed branches first (if not already seeded by migration)
        await SeedBranchesAsync(context, timeProvider);

        // Seed admin user
        await SeedAdminUserAsync(context, timeProvider);

        // Seed loan product checklist requirements
        await SeedLoanProductChecklistAsync(context);

        // Seed approval authority matrix
        await SeedApprovalAuthoritiesAsync(context);

        // Seed deviation severity catalog
        await SeedDeviationCatalogAsync(context);

        // Seed branch area codes
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
        var adminUser = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            FirstName = "James",
            MiddleName = "Jecemeco A.",
            LastName = "Tabilog",
            BranchId = "011", // Head Office Branch
            Role = "Admin",
            IsActive = true,
            CreatedAt = timeProvider.UtcNow
        };

        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
    }

    private static async Task SeedLoanProductChecklistAsync(AppDbContext context)
    {
        if (await context.LoanProductChecklists.AnyAsync())
            return;

        var checklistItems = new List<LoanProductChecklist>
        {
            // A16
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

            // A17
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

            // C02
            new() { LoanProduct = "C02", IdCode = "A1004" },
            new() { LoanProduct = "C02", IdCode = "A2008" },
            new() { LoanProduct = "C02", IdCode = "A2018" },
            new() { LoanProduct = "C02", IdCode = "A2019" },
            new() { LoanProduct = "C02", IdCode = "A2027" },
            new() { LoanProduct = "C02", IdCode = "A4001" },
            new() { LoanProduct = "C02", IdCode = "CCR38" },
            new() { LoanProduct = "C02", IdCode = "PIC02" },
            new() { LoanProduct = "C02", IdCode = "PIC03" },

            // C23
            new() { LoanProduct = "C23", IdCode = "A1004" },
            new() { LoanProduct = "C23", IdCode = "A2008" },
            new() { LoanProduct = "C23", IdCode = "A2018" },
            new() { LoanProduct = "C23", IdCode = "A2019" },
            new() { LoanProduct = "C23", IdCode = "A2027" },
            new() { LoanProduct = "C23", IdCode = "A4001" },
            new() { LoanProduct = "C23", IdCode = "CCR38" },
            new() { LoanProduct = "C23", IdCode = "PIC02" },
            new() { LoanProduct = "C23", IdCode = "PIC03" },

            // C35
            new() { LoanProduct = "C35", IdCode = "A1004" },
            new() { LoanProduct = "C35", IdCode = "A2008" },
            new() { LoanProduct = "C35", IdCode = "A2018" },
            new() { LoanProduct = "C35", IdCode = "A2019" },
            new() { LoanProduct = "C35", IdCode = "A2027" },
            new() { LoanProduct = "C35", IdCode = "A4001" },
            new() { LoanProduct = "C35", IdCode = "CCR38" },
            new() { LoanProduct = "C35", IdCode = "PIC02" },
            new() { LoanProduct = "C35", IdCode = "PIC03" },

            // C21
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
        if (await context.ApprovalAuthorities.AnyAsync())
            return;

        var authorities = new List<ApprovalAuthority>
        {
            // Tier 1: Branch Head + OIC Level 1 (Renewal only, max 300K)
            new() { Key = "BranchHead", DisplayName = "Branch Head", Tier = 1, Priority = 1,
                AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,
                MaxTotalExposure = 300_000, ScopeType = AuthorityScope.Branch },
            new() { Key = "OICLevel1", DisplayName = "OIC Level 1", Tier = 1, Priority = 2,
                AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,
                MaxTotalExposure = 300_000, ScopeType = AuthorityScope.Branch },

            // Tier 2: Area Head (Renewal only, max 600K, area scope)
            new() { Key = "AreaHead", DisplayName = "Area Head", Tier = 2, Priority = 1,
                AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,
                MaxTotalExposure = 600_000, ScopeType = AuthorityScope.Area },

            // Tier 3: RBG Head, Product Head, Credit Head (New+Renewal, Minor, max 1M)
            new() { Key = "RBGHead", DisplayName = "RBG Head", Tier = 3, Priority = 1,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,
                MaxTotalExposure = 1_000_000, ScopeType = AuthorityScope.Global },
            new() { Key = "ProductHead", DisplayName = "Product Head", Tier = 3, Priority = 2,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,
                MaxTotalExposure = 1_000_000, ScopeType = AuthorityScope.Global },
            new() { Key = "CreditHead", DisplayName = "Credit Head", Tier = 3, Priority = 3,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,
                MaxTotalExposure = 1_000_000, ScopeType = AuthorityScope.Global },

            // Tier 4: COO (New+Renewal, Major, max 1.2M)
            new() { Key = "COO", DisplayName = "Chief Operating Officer", Tier = 4, Priority = 1,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,
                MaxTotalExposure = 1_200_000, ScopeType = AuthorityScope.Global },

            // Tier 5: CEO, President, CreCom Chair Level D (New+Renewal, Major, max 1.5M)
            new() { Key = "CEO", DisplayName = "Chief Executive Officer", Tier = 5, Priority = 1,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,
                MaxTotalExposure = 1_500_000, ScopeType = AuthorityScope.Global },
            new() { Key = "President", DisplayName = "President", Tier = 5, Priority = 2,
                AllowNew = true, AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,
                MaxTotalExposure = 1_500_000, ScopeType = AuthorityScope.Global },
            new() { Key = "CreComChair", DisplayName = "CreCom Chair Level D", Tier = 5, Priority = 3,
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

        // Major deviations (from the bank's delegation matrix)
        var catalog = new List<DeviationCatalogItem>
        {
            // Major severity
            new() { Description = "Age not within the prescribed parameters", Severity = DeviationSeverity.Major },
            new() { Description = "Discounted Application Fee", Severity = DeviationSeverity.Major },
            new() { Description = "Interest rate reduction", Severity = DeviationSeverity.Major },
            new() { Description = "With past due account - non performing loan", Severity = DeviationSeverity.Major },

            // Minor severity (all other deviations)
            new() { Description = "Lacking bank statement of account", Severity = DeviationSeverity.Minor },
            new() { Description = "Lacking CIBI", Severity = DeviationSeverity.Minor },
            new() { Description = "Lacking marriage cert. with surname as single", Severity = DeviationSeverity.Minor },
            new() { Description = "Lacking one or two payslip(s) for new atm loan", Severity = DeviationSeverity.Minor },
            new() { Description = "Lacking signature in application form", Severity = DeviationSeverity.Minor },
            new() { Description = "Lacking SPAs to claim ATM", Severity = DeviationSeverity.Minor },
            new() { Description = "No appointment record and/or service record", Severity = DeviationSeverity.Minor },
            new() { Description = "No FI SOA and loan ledger", Severity = DeviationSeverity.Minor },
            new() { Description = "No latest payslip", Severity = DeviationSeverity.Minor },
            new() { Description = "No interview sheet", Severity = DeviationSeverity.Minor },
            new() { Description = "No orientation form or old form submitted", Severity = DeviationSeverity.Minor },
            new() { Description = "No valid identification cards", Severity = DeviationSeverity.Minor },
            new() { Description = "Total consumer loan exposure exceeding 1.2 million", Severity = DeviationSeverity.Minor },
            new() { Description = "With blocked ATIM in same school", Severity = DeviationSeverity.Minor },
            new() { Description = "With history of delinquency in the latest loan availment", Severity = DeviationSeverity.Minor },
            new() { Description = "With NFIS findings", Severity = DeviationSeverity.Minor },
            new() { Description = "With past due account - performing", Severity = DeviationSeverity.Minor },
        };

        context.DeviationCatalog.AddRange(catalog);
        await context.SaveChangesAsync();
    }

    private static async Task SeedBranchAreaCodesAsync(AppDbContext context)
    {
        // Area mapping from the bank's delegation matrix
        var areaMapping = new Dictionary<string, string>
        {
            // A1: San Francisco, Butuan, Cagayan, Trento, Bayugan, Nabunturan, Surigao, Gingoog
            ["003"] = "A1", ["008"] = "A1", ["012"] = "A1", ["023"] = "A1",
            ["025"] = "A1", ["026"] = "A1", ["028"] = "A1", ["029"] = "A1",

            // A2: Lianga, Barobo, Arasasan, Hinatuan, Bislig, Cateel, Madrid
            ["000"] = "A2", ["002"] = "A2", ["004"] = "A2", ["005"] = "A2",
            ["007"] = "A2", ["009"] = "A2", ["017"] = "A2", ["027"] = "A2",

            // A3: Tagum, General Santos, Panabo, Valencia, Davao-Matina, Mati
            ["006"] = "A3", ["011"] = "A3", ["014"] = "A3", ["015"] = "A3",
            ["016"] = "A3", ["022"] = "A3", ["024"] = "A3",

            // A4: Talisay, Tacloban, Bacolod, Iloilo, CTS (Mandaue), Ronda
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
