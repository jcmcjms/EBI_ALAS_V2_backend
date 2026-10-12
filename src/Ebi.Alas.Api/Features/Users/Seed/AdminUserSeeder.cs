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

        var firstName = seed.AdminFirstName?.Trim() ?? "System";
        var lastName = seed.AdminLastName?.Trim() ?? "Administrator";
        var hash = passwordHasher.Hash(seed.AdminPassword);
        var user = User.Create(
            userName,
            hash,
            firstName: firstName,
            lastName: lastName,
            middleName: string.Empty,
            email: seed.AdminEmail?.Trim() ?? string.Empty,
            branchId: seed.AdminBranchId.Trim(),
            role: UserRole.Admin,
            now: timeProvider.GetUtcNow());

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}