using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Approvals;

/// <summary>
/// Syncs a user's approval-authority membership from their job title.
/// Mutates the context only; callers own SaveChanges.
/// </summary>
public sealed class ApprovalAuthorityAssigner(AlasDbContext db)
{
    public async Task AssignAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var level = AuthorityJobTitles.Map(user.JobTitle);
        if (level is null || ApprovalAuthorityDefaults.For(level.Value) is not { } def)
        {
            return;
        }

        var authority = await db.ApprovalAuthorities
            .FirstOrDefaultAsync(a => a.Key == def.Key, cancellationToken);
        if (authority is null)
        {
            authority = ApprovalAuthorityDefaults.Create(def);
            db.ApprovalAuthorities.Add(authority);
        }

        var existing = await db.ApprovalAuthorityUsers
            .Where(x => x.UserId == user.Id)
            .ToListAsync(cancellationToken);
        db.ApprovalAuthorityUsers.RemoveRange(existing);

        db.ApprovalAuthorityUsers.Add(new ApprovalAuthorityUser
        {
            ApprovalAuthorityId = authority.Id,
            UserId = user.Id,
        });
    }
}
