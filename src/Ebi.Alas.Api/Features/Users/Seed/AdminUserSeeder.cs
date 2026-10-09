using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Features.Users.Seed;

public sealed class AdminUserSeeder(
    AlasDbContext db,
    PasswordHasher passwordHasher,
    IOptions<AdminSeedOptions> options,
    TimeProvider timeProvider)
{
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        var userName = seed.AdminUserName.Trim();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken);
        if (existing is not null)
        {
            return false;
        }

        var fullName = BuildFullName(seed);
        var hash = passwordHasher.Hash(seed.AdminPassword);
        var user = User.Create(
            userName,
            hash,
            fullName: fullName,
            email: seed.AdminEmail?.Trim() ?? string.Empty,
            branchId: seed.AdminBranchId.Trim(),
            role: UserRole.Admin,
            now: timeProvider.GetUtcNow());

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string BuildFullName(AdminSeedOptions seed)
    {
        var first = seed.AdminFirstName?.Trim();
        var last = seed.AdminLastName?.Trim();
        var combined = string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(combined) ? "System Administrator" : combined;
    }
}
