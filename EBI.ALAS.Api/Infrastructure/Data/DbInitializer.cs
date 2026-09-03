using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
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

        // Seed test users for each role
        await SeedTestUsersAsync(context, timeProvider);
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
            FirstName = "System",
            MiddleName = null,
            LastName = "Administrator",
            BranchId = "011", // Head Office Branch
            Role = "Admin",
            IsActive = true,
            CreatedAt = timeProvider.UtcNow
        };

        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
    }

    private static async Task SeedTestUsersAsync(AppDbContext context, ITimeProvider timeProvider)
    {
        var now = timeProvider.UtcNow;
        var testUsers = new List<User>
        {
            new User
            {
                Username = "encoder1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("encoder123"),
                FirstName = "Juan",
                MiddleName = "D.",
                LastName = "Cruz",
                BranchId = "007", // Tandag Branch
                Role = "Encoder",
                IsActive = true,
                CreatedAt = now
            },
            new User
            {
                Username = "recommender1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("recommender123"),
                FirstName = "Maria",
                MiddleName = "S.",
                LastName = "Santos",
                BranchId = "007", // Tandag Branch
                Role = "Recommender",
                IsActive = true,
                CreatedAt = now
            },
            new User
            {
                Username = "evaluator1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("evaluator123"),
                FirstName = "Pedro",
                MiddleName = "M.",
                LastName = "Garcia",
                BranchId = "007", // Tandag Branch
                Role = "Evaluator",
                IsActive = true,
                CreatedAt = now
            },
            new User
            {
                Username = "approver1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("approver123"),
                FirstName = "Ana",
                MiddleName = "L.",
                LastName = "Reyes",
                BranchId = "007", // Tandag Branch
                Role = "Approver",
                IsActive = true,
                CreatedAt = now
            }
        };

        context.Users.AddRange(testUsers);
        await context.SaveChangesAsync();
    }
}
