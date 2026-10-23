using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Approvals;

/// <summary>
/// Seeds the approval catalog with the officer authorities and the limits the
/// legacy approval matrix enforced before the catalog existed. Existing
/// approvers are assigned to the authority matching their job title, so
/// routing behaviour is unchanged once the catalog takes over.
/// </summary>
public sealed class ApprovalAuthoritySeeder(AlasDbContext db)
{
    private static readonly (AuthorityLevel Level, string DisplayName, int Tier, bool AllowNew, bool AllowRenewal, int MaxSeverity, decimal MaxExposure, int ScopeType)[] Defaults =
    [
        (AuthorityLevel.BranchHead, "Branch Head", 1, false, true, 0, 300_000m, AuthorityScopes.Branch),
        (AuthorityLevel.OfficerInCharge, "Officer In Charge", 1, false, true, 0, 300_000m, AuthorityScopes.Branch),
        (AuthorityLevel.AreaHead, "Area Head", 2, false, true, 0, 600_000m, AuthorityScopes.Area),
        (AuthorityLevel.RbgHead, "RBG Head", 3, true, true, 1, 1_000_000m, AuthorityScopes.Global),
        (AuthorityLevel.ProductHead, "Product Head", 3, true, true, 1, 1_000_000m, AuthorityScopes.Global),
        (AuthorityLevel.CreditHead, "Credit Head", 3, true, true, 1, 1_000_000m, AuthorityScopes.Global),
        (AuthorityLevel.Coo, "COO", 4, true, true, 2, 1_200_000m, AuthorityScopes.Global),
        (AuthorityLevel.PresidentCeo, "President / CEO", 5, true, true, 2, 1_500_000m, AuthorityScopes.Global),
        (AuthorityLevel.CreComBod, "CreCom / Board", 6, true, true, 2, 10_000_000m, AuthorityScopes.Global),
    ];

    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.ApprovalAuthorities.AsNoTracking().AnyAsync(cancellationToken))
        {
            return false;
        }

        var authorities = new Dictionary<AuthorityLevel, ApprovalAuthority>();
        foreach (var (level, displayName, tier, allowNew, allowRenewal, maxSeverity, maxExposure, scopeType) in Defaults)
        {
            var authority = ApprovalAuthority.Create(
                displayName,
                tier,
                0,
                allowNew,
                allowRenewal,
                maxSeverity,
                maxExposure,
                scopeType);
            db.ApprovalAuthorities.Add(authority);
            authorities[level] = authority;
        }

        var approvers = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Approver)
            .Select(u => new { u.Id, u.JobTitle })
            .ToListAsync(cancellationToken);

        foreach (var approver in approvers)
        {
            var level = AuthorityJobTitles.Map(approver.JobTitle);
            if (level is null || !authorities.TryGetValue(level.Value, out var authority))
            {
                continue;
            }

            db.ApprovalAuthorityUsers.Add(new ApprovalAuthorityUser
            {
                ApprovalAuthorityId = authority.Id,
                UserId = approver.Id,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
