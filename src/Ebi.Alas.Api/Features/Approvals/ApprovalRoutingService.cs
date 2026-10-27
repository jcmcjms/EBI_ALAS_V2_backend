using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Approvals;

public sealed record CatalogCandidate(Guid UserId, string Name);

public sealed record CatalogRoutingResult(
    int RequiredTier,
    string MatchedRule,
    int? EscalatedFromTier,
    string? NoAuthorityReason,
    bool SharedDesk,
    IReadOnlyList<CatalogCandidate> Candidates);

/// <summary>
/// Resolves the required approval authority from the configured catalog
/// (authorities with assigned, available approvers). Returns null when no
/// authorities are configured so callers can fall back to the legacy matrix.
/// </summary>
public sealed class ApprovalRoutingService(AlasDbContext db)
{
    public async Task<CatalogRoutingResult?> TryResolveAsync(
        LoanApplication loan,
        int maxSeverity,
        bool hasRatePriceDeviation,
        CancellationToken ct)
    {
        if (hasRatePriceDeviation)
        {
            return new CatalogRoutingResult(
                (int)AuthorityLevel.CreComBod,
                ApprovalMatrix.Describe(AuthorityLevel.CreComBod),
                null,
                "Rate or price deviations need CreCom approval (up to ₱1.5M, new or renewal).",
                false,
                []);
        }

        var authorities = await db.ApprovalAuthorities.AsNoTracking()
            .OrderBy(a => a.Tier)
            .ThenBy(a => a.Priority)
            .ToListAsync(ct);

        if (authorities.Count == 0)
        {
            return null;
        }

        var (candidatesByAuthority, memberCounts) = await LoadCandidatesAsync(ct);
        if (candidatesByAuthority.Count == 0)
        {
            // No authority has an available assignee — the catalog cannot route
            // anything, so the legacy matrix stays in charge.
            return null;
        }

        var areaByBranch = await db.Branches.AsNoTracking()
            .Where(b => b.AreaCode != null && b.AreaCode != "")
            .ToDictionaryAsync(b => b.Code, b => b.AreaCode!, ct);
        var loanArea = await db.Branches.AsNoTracking()
            .Where(b => b.Code == loan.BranchId)
            .Select(b => b.AreaCode)
            .FirstOrDefaultAsync(ct);

        var exposure = loan.Principal + loan.TotalInterest;
        var isReloan = loan.LoanType == LoanType.Reloan;
        int? escalatedFrom = null;

        foreach (var authority in authorities)
        {
            var covers = authority.MaxSeverity >= maxSeverity
                && authority.MaxTotalExposure >= exposure
                && (isReloan ? authority.AllowRenewal : authority.AllowNew);

            if (!covers)
            {
                continue;
            }

            var candidates = InScope(
                authority,
                candidatesByAuthority.GetValueOrDefault(authority.Id, []),
                areaByBranch,
                loan.BranchId,
                loanArea);

            if (candidates.Count == 0)
            {
                escalatedFrom ??= authority.Tier;
                continue;
            }

            return new CatalogRoutingResult(
                authority.Tier,
                authority.DisplayName,
                escalatedFrom,
                null,
                memberCounts.GetValueOrDefault(authority.Id) > 1,
                candidates);
        }

        return new CatalogRoutingResult(
            0,
            "None",
            escalatedFrom,
            "No approval tier covers this loan. An administrator can adjust tiers under Approval authorities.",
            false,
            []);
    }

    private async Task<(Dictionary<Guid, List<CandidateRow>> Candidates, Dictionary<Guid, int> MemberCounts)> LoadCandidatesAsync(
        CancellationToken ct)
    {
        var assignments = await db.ApprovalAuthorityUsers.AsNoTracking().ToListAsync(ct);
        var memberCounts = assignments
            .GroupBy(x => x.ApprovalAuthorityId)
            .ToDictionary(g => g.Key, g => g.Count());

        var userIds = assignments.Select(x => x.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)
                && u.Role == UserRole.Approver
                && u.Status == UserStatus.Active
                && u.IsAvailable)
            .Select(u => new { u.Id, u.BranchId, Name = (u.FirstName + " " + u.LastName).Trim() })
            .ToDictionaryAsync(u => u.Id, ct);

        var candidates = assignments
            .Where(x => users.ContainsKey(x.UserId))
            .GroupBy(x => x.ApprovalAuthorityId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new CandidateRow(x.UserId, users[x.UserId].Name, users[x.UserId].BranchId)).ToList());

        return (candidates, memberCounts);
    }

    private static List<CatalogCandidate> InScope(
        ApprovalAuthority authority,
        List<CandidateRow> candidates,
        Dictionary<string, string> areaByBranch,
        string loanBranch,
        string? loanArea)
    {
        return candidates
            .Where(c => authority.ScopeType switch
            {
                AuthorityScopes.Global => true,
                AuthorityScopes.Area => loanArea is not null
                    && areaByBranch.TryGetValue(c.BranchId, out var userArea)
                    && userArea == loanArea,
                AuthorityScopes.Branch => c.BranchId == loanBranch,
                _ => false,
            })
            .Select(c => new CatalogCandidate(c.UserId, c.Name))
            .ToList();
    }

    private sealed record CandidateRow(Guid UserId, string Name, string BranchId);
}
