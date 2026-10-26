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
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.ApprovalAuthorities.AsNoTracking().AnyAsync(cancellationToken))
        {
            return false;
        }

        var authorities = new Dictionary<AuthorityLevel, ApprovalAuthority>();
        foreach (var def in ApprovalAuthorityDefaults.All)
        {
            var authority = ApprovalAuthorityDefaults.Create(def);
            db.ApprovalAuthorities.Add(authority);
            authorities[def.Level] = authority;
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
