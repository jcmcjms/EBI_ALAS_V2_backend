using EBI.ALAS.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Infrastructure.Data;

/// <summary>
/// Database initializer that seeds default data including admin user.
/// Only runs if the database is empty.
/// </summary>
public static class DbInitializer
{
    /// <summary>
    /// Initializes the database with seed data if it's empty.
    /// </summary>
    public static async Task InitializeAsync(AppDbContext context, IServiceProvider serviceProvider)
    {
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();

        // Check if database already has data
        if (await context.Users.AnyAsync())
        {
            return; // Database already seeded
        }

        // Seed admin user
        await SeedAdminUserAsync(context);

        // Seed test users for each role
        await SeedTestUsersAsync(context);
    }

    private static async Task SeedAdminUserAsync(AppDbContext context)
    {
        var adminUser = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            FirstName = "System",
            MiddleName = null,
            LastName = "Administrator",
            BranchId = "HO",
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
    }

    private static async Task SeedTestUsersAsync(AppDbContext context)
    {
        var testUsers = new List<User>
        {
            new User
            {
                Username = "encoder1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("encoder123"),
                FirstName = "Juan",
                MiddleName = "D.",
                LastName = "Cruz",
                BranchId = "BR001",
                Role = "Encoder",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Username = "recommender1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("recommender123"),
                FirstName = "Maria",
                MiddleName = "S.",
                LastName = "Santos",
                BranchId = "BR001",
                Role = "Recommender",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Username = "evaluator1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("evaluator123"),
                FirstName = "Pedro",
                MiddleName = "M.",
                LastName = "Garcia",
                BranchId = "BR001",
                Role = "Evaluator",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Username = "approver1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("approver123"),
                FirstName = "Ana",
                MiddleName = "L.",
                LastName = "Reyes",
                BranchId = "BR001",
                Role = "Approver",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        context.Users.AddRange(testUsers);
        await context.SaveChangesAsync();
    }
}
