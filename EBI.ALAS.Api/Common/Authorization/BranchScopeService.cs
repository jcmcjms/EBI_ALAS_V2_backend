using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Infrastructure.Data;

namespace EBI.ALAS.Api.Common.Authorization;

/// <summary>
/// Branch scope service interface. Returns the set of branch codes a user may read.
/// </summary>
public interface IBranchScopeService
{
    /// <summary>
    /// Returns the set of branch codes the user may read, or null when unrestricted.
    /// Never returns an empty set — an empty scope is a misconfiguration.
    /// </summary>
    Task<IReadOnlySet<string>?> GetReadableBranchesAsync(
        ClaimsPrincipal user, CancellationToken ct = default);

    /// <summary>
    /// Sync check for URL-derived branches (webloan drill-downs).
    /// </summary>
    Task<bool> CanAccessBranchAsync(
        ClaimsPrincipal user, string branchCode, CancellationToken ct = default);
}

/// <summary>
/// Resolves branch scope based on user role and approval authority.
/// Uses primary constructor for dependency injection.
/// </summary>
public sealed class BranchScopeService(AppDbContext db) : IBranchScopeService
{
    public async Task<IReadOnlySet<string>?> GetReadableBranchesAsync(
        ClaimsPrincipal user, CancellationToken ct = default)
    {
        var role = user.GetRole();
        var home = user.GetBranchCode();

        if (role == Roles.Admin) return null;

        if (role != Roles.Approver)
            return FailSafe(home, null);

        var userId = user.GetUserId();
        var authority = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ApprovalAuthority)
            .FirstOrDefaultAsync(ct);

        if (authority is null) return FailSafe(home, null);

        return authority.ScopeType switch
        {
            AuthorityScope.Global => null,
            AuthorityScope.Area => await ResolveAreaScopeAsync(home, ct),
            AuthorityScope.Branch => await ResolveBranchScopeAsync(userId, home, ct),
            _ => FailSafe(home, null),
        };
    }

    public async Task<bool> CanAccessBranchAsync(
        ClaimsPrincipal user, string branchCode, CancellationToken ct = default)
    {
        var scope = await GetReadableBranchesAsync(user, ct);
        return scope is null || scope.Contains(branchCode);
    }

    private async Task<IReadOnlySet<string>> ResolveAreaScopeAsync(
        string home, CancellationToken ct)
    {
        var areaCode = await db.Branches
            .AsNoTracking()
            .Where(b => b.Code == home)
            .Select(b => b.AreaCode)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(areaCode))
            return FailSafe(home, null);

        var areaBranches = await db.Branches
            .AsNoTracking()
            .Where(b => b.AreaCode == areaCode)
            .Select(b => b.Code)
            .ToListAsync(ct);

        return FailSafe(home, new HashSet<string>(areaBranches, StringComparer.Ordinal));
    }

    private async Task<IReadOnlySet<string>> ResolveBranchScopeAsync(
        int userId, string home, CancellationToken ct)
    {
        var covered = await db.UserBranchCoverages
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.BranchCode)
            .ToListAsync(ct);

        return FailSafe(home, new HashSet<string>(covered, StringComparer.Ordinal));
    }

    /// <summary>
    /// Coverage is additive to the home branch, never a replacement.
    /// Empty result falls back to home; empty home throws (misconfigured account).
    /// </summary>
    private static IReadOnlySet<string> FailSafe(
        string home, IReadOnlySet<string>? extra)
    {
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException(
                "Authenticated account has no branch assignment. Contact administration.");

        var set = new HashSet<string>(StringComparer.Ordinal) { home };
        if (extra is not null) set.UnionWith(extra);
        return set;
    }
}
