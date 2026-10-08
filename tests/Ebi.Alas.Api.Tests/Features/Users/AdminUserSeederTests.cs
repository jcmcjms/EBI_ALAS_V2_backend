using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.Seed;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class AdminUserSeederTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"admin-seed-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static AdminUserSeeder Seeder(AlasDbContext db) => new(
        db,
        new PasswordHasher(),
        Options.Create(new AdminSeedOptions
        {
            AdminUserName = "seed-admin",
            AdminPassword = "SeedPass!23456",
            AdminBranchId = "BR-HQ",
            AdminFirstName = "System",
            AdminLastName = "Administrator",
            AdminEmail = "admin@example.invalid"
        }),
        TimeProvider.System);

    [Fact]
    public async Task SeedAsync_CreatesAdminUser()
    {
        await using var db = Db();
        var created = await Seeder(db).SeedAsync(CancellationToken.None);

        Assert.True(created);
        var user = await db.Users.SingleAsync(u => u.UserName == "seed-admin");
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal("BR-HQ", user.BranchId);
        Assert.Equal("System Administrator", user.FullName);
        Assert.True(user.MustChangePassword);
        Assert.NotEqual("SeedPass!23456", user.PasswordHash);
    }

    [Fact]
    public async Task SeedAsync_ExistingUsername_DoesNotDuplicateOrResetPassword()
    {
        await using var db = Db();
        var seeder = Seeder(db);
        await seeder.SeedAsync(CancellationToken.None);
        var first = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "seed-admin");

        var createdAgain = await seeder.SeedAsync(CancellationToken.None);
        var second = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "seed-admin");

        Assert.False(createdAgain);
        Assert.Equal(first.PasswordHash, second.PasswordHash);
        Assert.Equal(1, await db.Users.CountAsync(u => u.UserName == "seed-admin"));
    }

    [Fact]
    public async Task SeedAsync_ExistingUserWithStalePassword_UpdatesHashToMatchSecrets()
    {
        await using var db = Db();
        var seeder = Seeder(db);
        await seeder.SeedAsync(CancellationToken.None);

        var user = await db.Users.SingleAsync(u => u.UserName == "seed-admin");
        user.SetPasswordHash(new PasswordHasher().Hash("OldPassword!234"), mustChangePassword: false, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var updated = await seeder.SeedAsync(CancellationToken.None);

        Assert.True(updated);
        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "seed-admin");
        Assert.Equal(1, await db.Users.CountAsync(u => u.UserName == "seed-admin"));
        Assert.True(new PasswordHasher().Verify("SeedPass!23456", reloaded.PasswordHash));
        Assert.True(reloaded.MustChangePassword);
    }
}
