using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Approvals;

/// <summary>
/// The scope an officer holds through the approval catalog: which branches they
/// may read, and whether their desk is shared with other officers.
/// </summary>
public sealed record OfficerScope(int ScopeType, string? AreaCode, bool SharedDesk);

public sealed record ScopeResolution(
    bool CatalogConfigured,
    IReadOnlyDictionary<Guid, OfficerScope> Scopes)
{
    public OfficerScope ScopeFor(Guid userId) =>
        Scopes.GetValueOrDefault(userId, ApprovalScopeService.BranchScope);
}

/// <summary>
/// Resolves officer scope from the approval catalog. When no authorities are
/// configured the catalog is not authoritative and callers fall back to the
/// legacy job-title rules; once configured, an unassigned officer holds only
/// branch scope.
/// </summary>
public sealed class ApprovalScopeService(AlasDbContext db)
{
    public static readonly OfficerScope BranchScope = new(AuthorityScopes.Branch, null, false);

    /// <summary>
    /// The catalog is authoritative only once an authority has an assignee —
    /// an unstaffed catalog cannot route anything, so the legacy matrix stays
    /// in charge until officers are assigned.
    /// </summary>
    public async Task<bool> CatalogConfiguredAsync(CancellationToken ct) =>
        await db.ApprovalAuthorityUsers.AsNoTracking().AnyAsync(ct);

    public async Task<ScopeResolution> ResolveAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct)
    {
        if (!await CatalogConfiguredAsync(ct))
        {
            return new ScopeResolution(false, new Dictionary<Guid, OfficerScope>());
        }

        if (userIds.Count == 0)
        {
            return new ScopeResolution(true, new Dictionary<Guid, OfficerScope>());
        }

        var assignments = await db.ApprovalAuthorityUsers.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .ToListAsync(ct);
        var authorityIds = assignments.Select(x => x.ApprovalAuthorityId).Distinct().ToList();
        var authorities = await db.ApprovalAuthorities.AsNoTracking()
            .Where(a => authorityIds.Contains(a.Id))
            .ToListAsync(ct);
        if (authorities.Count == 0)
        {
            return new ScopeResolution(true, new Dictionary<Guid, OfficerScope>());
        }

        var memberCounts = await MemberCountsAsync(authorityIds, ct);
        var byAuthority = authorities.ToDictionary(a => a.Id);
        var officers = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.BranchId })
            .ToListAsync(ct);
        var homeArea = await db.Branches.AsNoTracking()
            .Where(b => officers.Select(o => o.BranchId).Contains(b.Code))
            .Select(b => new { b.Code, b.AreaCode })
            .ToDictionaryAsync(b => b.Code, b => b.AreaCode, ct);

        var scopes = new Dictionary<Guid, OfficerScope>();
        foreach (var officer in officers)
        {
            var owned = assignments
                .Where(x => x.UserId == officer.Id)
                .Select(x => byAuthority[x.ApprovalAuthorityId])
                .ToList();
            if (owned.Count == 0)
            {
                continue;
            }

            // Widest scope wins; ties break on the lower tier.
            var chosen = owned
                .OrderByDescending(a => a.ScopeType)
                .ThenBy(a => a.Tier)
                .First();

            homeArea.TryGetValue(officer.BranchId, out var area);
            scopes[officer.Id] = new OfficerScope(
                chosen.ScopeType,
                string.IsNullOrWhiteSpace(area) ? null : area,
                memberCounts.GetValueOrDefault(chosen.Id) > 1);
        }

        return new ScopeResolution(true, scopes);
    }

    public async Task<OfficerScope?> TryResolveAsync(Guid userId, CancellationToken ct)
    {
        var resolution = await ResolveAsync([userId], ct);
        return resolution.CatalogConfigured ? resolution.ScopeFor(userId) : null;
    }

    /// <summary>
    /// Officers who share a desk — an authority with more than one assignee.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSharedDeskMemberNamesAsync(CancellationToken ct)
    {
        var memberCounts = await MemberCountsAsync(null, ct);
        var sharedIds = memberCounts.Where(x => x.Value > 1).Select(x => x.Key).ToList();
        if (sharedIds.Count == 0)
        {
            return [];
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Approver && u.Status == UserStatus.Active && u.IsAvailable)
            .Where(u => db.ApprovalAuthorityUsers
                .Any(a => a.UserId == u.Id && sharedIds.Contains(a.ApprovalAuthorityId)))
            .Select(u => (u.FirstName + " " + u.LastName).Trim())
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> MemberCountsAsync(
        IReadOnlyCollection<Guid>? authorityIds,
        CancellationToken ct)
    {
        var query = db.ApprovalAuthorityUsers.AsNoTracking();
        if (authorityIds is not null)
        {
            query = query.Where(x => authorityIds.Contains(x.ApprovalAuthorityId));
        }

        return await query
            .GroupBy(x => x.ApprovalAuthorityId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }
}
