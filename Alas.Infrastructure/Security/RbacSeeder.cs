using Alas.Application.Common.Security;
using Alas.Infrastructure.Identity;
using Alas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alas.Infrastructure.Security;

public static class RbacSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var context = scope.ServiceProvider.GetRequiredService<AlasDbContext>();

        await SeedRolesAsync(roleManager);
        await SeedRolePermissionsAsync(context);
    }

    private static async Task SeedRolesAsync(RoleManager<AppRole> roleManager)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AlasRoles.SuperAdmin] = "Full system access with all permissions",
            [AlasRoles.Admin] = "Manages users, roles, and system settings",
            [AlasRoles.LoanManager] = "Approves and monitors loan applications",
            [AlasRoles.LoanOfficer] = "Creates and processes loan applications",
            [AlasRoles.Auditor] = "Read-only access to loans and audit logs"
        };

        foreach (var roleName in AlasRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new AppRole
                {
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant(),
                    Description = descriptions.GetValueOrDefault(roleName, $"{roleName} role")
                });
            }
        }
    }

    private static async Task SeedRolePermissionsAsync(AlasDbContext context)
    {
        foreach (var entry in AlasRoles.Matrix)
        {
            var roleName = entry.Key;
            var permissions = entry.Value;

            var role = await context.Roles
                .FirstOrDefaultAsync(r => r.Name == roleName);

            if (role is null)
            {
                continue;
            }

            foreach (var permission in permissions)
            {
                var exists = await context.RoleClaims
                    .AnyAsync(rc =>
                        rc.RoleId == role.Id &&
                        rc.ClaimType == AlasClaimTypes.Permission &&
                        rc.ClaimValue == permission);

                if (!exists)
                {
                    context.RoleClaims.Add(new IdentityRoleClaim<Guid>
                    {
                        RoleId = role.Id,
                        ClaimType = AlasClaimTypes.Permission,
                        ClaimValue = permission
                    });
                }
            }
        }

        await context.SaveChangesAsync();
    }
}
