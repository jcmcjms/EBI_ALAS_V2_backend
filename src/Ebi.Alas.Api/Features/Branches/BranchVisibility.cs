using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Branches;

public static class BranchVisibility
{
    public static bool IsAreaHead(string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            return false;
        }

        var compact = new string(jobTitle.Where(char.IsLetterOrDigit).ToArray());
        return compact.Equals("AreaHead", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("AreaHeadLevelH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Org-wide loan visibility for officers whose board authority is not branch-bound
    /// (RBG / Product / Credit Head, President, COO, CreCom).
    /// </summary>
    public static bool IsGlobalAuthority(string? jobTitle)
    {
        var level = AuthorityJobTitles.Map(jobTitle);
        return level is AuthorityLevel.RbgHead
            or AuthorityLevel.ProductHead
            or AuthorityLevel.CreditHead
            or AuthorityLevel.Coo
            or AuthorityLevel.PresidentCeo
            or AuthorityLevel.CreComBod;
    }

    /// <summary>
    /// Catalog-aware global authority: the officer is assigned to a global-scope
    /// authority. Falls back to the job title while no authorities are configured.
    /// </summary>
    public static async Task<bool> IsGlobalAuthorityAsync(
        AlasDbContext db,
        Guid userId,
        string? jobTitle,
        CancellationToken cancellationToken)
    {
        var resolution = await new ApprovalScopeService(db).ResolveAsync([userId], cancellationToken);
        return resolution.CatalogConfigured
            ? resolution.ScopeFor(userId).ScopeType == AuthorityScopes.Global
            : IsGlobalAuthority(jobTitle);
    }

    /// <summary>
    /// Branches an officer may read under a catalog scope: global officers see
    /// every branch, area officers their area, branch officers their own branch.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetReadableBranchesAsync(
        AlasDbContext db,
        OfficerScope scope,
        string branchId,
        CancellationToken cancellationToken)
    {
        var home = (branchId ?? string.Empty).Trim();
        switch (scope.ScopeType)
        {
            case AuthorityScopes.Global:
                return await db.Branches.AsNoTracking()
                    .Select(b => b.Code)
                    .ToListAsync(cancellationToken);
            case AuthorityScopes.Area:
                if (string.IsNullOrEmpty(home) || scope.AreaCode is null)
                {
                    return string.IsNullOrEmpty(home) ? [] : [home];
                }

                var areaCodes = await db.Branches.AsNoTracking()
                    .Where(b => b.AreaCode == scope.AreaCode)
                    .Select(b => b.Code)
                    .ToListAsync(cancellationToken);
                return areaCodes.Contains(home) ? areaCodes : areaCodes.Append(home).ToList();
            default:
                return string.IsNullOrEmpty(home) ? [] : [home];
        }
    }

    public static async Task<IReadOnlyList<string>> GetReadableBranchIdsAsync(
        AlasDbContext db,
        Guid userId,
        UserRole role,
        string branchId,
        CancellationToken cancellationToken,
        string? jobTitle = null)
    {
        var home = (branchId ?? string.Empty).Trim();

        var resolution = await new ApprovalScopeService(db).ResolveAsync([userId], cancellationToken);
        if (resolution.CatalogConfigured)
        {
            if (role is UserRole.Admin or UserRole.System or UserRole.Evaluator)
            {
                return await db.Branches.AsNoTracking()
                    .Select(b => b.Code)
                    .ToListAsync(cancellationToken);
            }

            return await GetReadableBranchesAsync(db, resolution.ScopeFor(userId), home, cancellationToken);
        }

        if (role is UserRole.Admin or UserRole.System or UserRole.Evaluator
            || IsGlobalAuthority(jobTitle))
        {
            return await db.Branches.AsNoTracking()
                .Select(b => b.Code)
                .ToListAsync(cancellationToken);
        }

        if (!IsAreaHead(jobTitle))
        {
            return string.IsNullOrEmpty(home) ? [] : [home];
        }

        if (string.IsNullOrEmpty(home))
        {
            return [];
        }

        var areaCode = await db.Branches.AsNoTracking()
            .Where(b => b.Code == home)
            .Select(b => b.AreaCode)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(areaCode))
        {
            return [home];
        }

        var codes = await db.Branches.AsNoTracking()
            .Where(b => b.AreaCode == areaCode)
            .Select(b => b.Code)
            .ToListAsync(cancellationToken);
        return codes.Contains(home) ? codes : codes.Append(home).ToList();
    }
}
