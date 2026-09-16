using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Common.Authorization;

public interface IBranchScopeService
{
    /// <summary>
    /// Returns the set of branch codes the user may read, or <c>null</c>
    /// when the user is unrestricted (Admin, Global-scope approver).
    ///
    /// <b>Never returns an empty set</b> — an empty scope is a
    /// misconfiguration, not a permission state, and must never reach a
    /// SQL predicate. When coverage rows are missing, the user's home
    /// branch is always included as a floor. When the home branch itself
    /// is missing, the service throws so we fail LOUD instead of
    /// returning zero rows.
    /// </summary>
    Task<IReadOnlySet<string>?> GetReadableBranchesAsync(
        ClaimsPrincipal user, CancellationToken ct = default);

    /// <summary>
    /// Sync check for URL-derived branches (webloan drill-downs).
    /// </summary>
    Task<bool> CanAccessBranchAsync(
        ClaimsPrincipal user, string branchCode, CancellationToken ct = default);
}

public sealed class BranchScopeService : IBranchScopeService
{
    private readonly AppDbContext _db;

    public BranchScopeService(AppDbContext db) => _db = db;

    public async Task<IReadOnlySet<string>?> GetReadableBranchesAsync(
        ClaimsPrincipal user, CancellationToken ct = default)
    {
        var role = user.GetRole();
        var home = user.GetBranchCode();

        // Admin: unrestricted — no branch predicate at all.
        if (role == Roles.Admin) return null;

        // Non-approver roles (Encoder, Recommender, Evaluator):
        // home branch only, no coverage table consulted.
        if (role != Roles.Approver)
            return FailSafe(home, null);

        // Approver: resolve authority → scope type → branch set.
        var userId = user.GetUserId();
        var authority = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ApprovalAuthority)
            .FirstOrDefaultAsync(ct);

        // Approver without an authority row: fall back to home branch.
        // This is a misconfiguration but not a security hole — the
        // approver simply cannot see beyond their own branch.
        if (authority is null) return FailSafe(home, null);

        return authority.ScopeType switch
        {
            // Global scope: unrestricted (same as Admin for reads).
            AuthorityScope.Global => null,

            // Area scope: all branches sharing the home branch's AreaCode.
            AuthorityScope.Area => await ResolveAreaScopeAsync(home, ct),

            // Branch scope: home + explicit coverage rows.
            AuthorityScope.Branch => await ResolveBranchScopeAsync(userId, home, ct),

            // Unknown scope type: fall back to home branch.
            _ => FailSafe(home, null),
        };
    }

    public async Task<bool> CanAccessBranchAsync(
        ClaimsPrincipal user, string branchCode, CancellationToken ct = default)
    {
        var scope = await GetReadableBranchesAsync(user, ct);
        // null = unrestricted; otherwise check membership.
        return scope is null || scope.Contains(branchCode);
    }

    // ─── Private helpers ────────────────────────────────────────────────

    /// <summary>
    /// Resolve Area scope: find the home branch's AreaCode, then select
    /// all active branches with that same AreaCode.
    /// </summary>
    private async Task<IReadOnlySet<string>> ResolveAreaScopeAsync(
        string home, CancellationToken ct)
    {
        // Step 1: find the home branch's AreaCode.
        var areaCode = await _db.Branches
            .AsNoTracking()
            .Where(b => b.Code == home)
            .Select(b => b.AreaCode)
            .FirstOrDefaultAsync(ct);

        // Home branch has no AreaCode assignment: fall back to home only.
        if (string.IsNullOrWhiteSpace(areaCode))
            return FailSafe(home, null);

        // Step 2: all branches in that area.
        var areaBranches = await _db.Branches
            .AsNoTracking()
            .Where(b => b.AreaCode == areaCode)
            .Select(b => b.Code)
            .ToListAsync(ct);

        return FailSafe(home, new HashSet<string>(areaBranches, StringComparer.Ordinal));
    }

    /// <summary>
    /// Resolve Branch scope: home branch + explicit coverage rows from
    /// UserBranchCoverage.
    /// </summary>
    private async Task<IReadOnlySet<string>> ResolveBranchScopeAsync(
        int userId, string home, CancellationToken ct)
    {
        var covered = await _db.UserBranchCoverages
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.BranchCode)
            .ToListAsync(ct);

        return FailSafe(home, new HashSet<string>(covered, StringComparer.Ordinal));
    }

    /// <summary>
    /// Coverage is additive to the home branch, never a replacement.
    /// Empty result ⇒ fall back to home; empty home ⇒ throw
    /// (misconfigured account) so we fail LOUD instead of returning
    /// zero rows.
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
