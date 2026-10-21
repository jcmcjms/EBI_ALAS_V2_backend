using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Approvals;

public sealed record AssignmentCandidate(Guid UserId, string Name, string? JobTitle);

/// <summary>
/// Result of authority routing. <see cref="AssignedUserId"/> is a single exclusive
/// assignee. <see cref="SharedPool"/> is the open set of officers (RBG / Product /
/// Credit Head) who all appear as assigned — first to serve acts.
/// </summary>
public sealed record AssignmentResult(
    Guid? AssignedUserId,
    AuthorityLevel RequiredLevel,
    AuthorityLevel? EscalatedFromLevel,
    string? NoAuthorityReason,
    string MatchedRule,
    IReadOnlyList<AssignmentCandidate> SharedPool);

public sealed class ApproverAssignmentService(AlasDbContext db)
{
    public async Task<AssignmentResult> PickAsync(
        LoanApplication loan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loan);

        var descriptions = await db.LoanDeviations.AsNoTracking()
            .Where(d => d.LoanApplicationId == loan.Id)
            .Select(d => new { d.Description, d.Severity })
            .ToListAsync(cancellationToken);

        var maxSeverity = descriptions.Count == 0
            ? 0
            : descriptions.Max(d => d.Severity);
        var ratePrice = descriptions.Any(d => ApprovalMatrix.IsRatePriceDeviation(d.Description));
        var exposure = loan.Principal + loan.TotalInterest;
        var decision = ApprovalMatrix.Resolve(new ApprovalInputs(
            loan.LoanType,
            maxSeverity,
            exposure,
            ratePrice));

        if (decision.NoAuthorityReason is not null)
        {
            return new AssignmentResult(
                null,
                decision.RequiredLevel,
                decision.EscalatedFromLevel,
                decision.NoAuthorityReason,
                decision.MatchedRule,
                []);
        }

        var candidates = await CandidatesAsync(loan, decision.RequiredLevel, cancellationToken);
        if (candidates.Count == 0)
        {
            return new AssignmentResult(
                null,
                decision.RequiredLevel,
                decision.EscalatedFromLevel,
                "No staffed approver covers the required authority level.",
                decision.MatchedRule,
                []);
        }

        // RBG / Product / Credit Head share the desk: everyone is assigned; first to serve acts.
        if (decision.RequiredLevel == AuthorityLevel.RbgProductCredit)
        {
            var shared = candidates
                .Where(c => c.Level == AuthorityLevel.RbgProductCredit)
                .OrderBy(c => c.JobTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new AssignmentCandidate(c.Id, c.Name, c.JobTitle))
                .ToList();

            if (shared.Count > 0)
            {
                return new AssignmentResult(
                    null,
                    decision.RequiredLevel,
                    decision.EscalatedFromLevel,
                    null,
                    decision.MatchedRule,
                    shared);
            }
        }

        var pick = candidates
            .OrderBy(c => (int)c.Level)
            .ThenBy(c => c.Load)
            .ThenBy(c => c.Id)
            .First();

        return new AssignmentResult(
            pick.Id,
            decision.RequiredLevel,
            pick.Level > decision.RequiredLevel ? decision.RequiredLevel : decision.EscalatedFromLevel,
            null,
            decision.MatchedRule,
            [new AssignmentCandidate(pick.Id, pick.Name, pick.JobTitle)]);
    }

    private async Task<List<(Guid Id, string Name, string? JobTitle, AuthorityLevel Level, int Load)>> CandidatesAsync(
        LoanApplication loan,
        AuthorityLevel required,
        CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Approver && u.Status == UserStatus.Active && u.IsAvailable)
            .Select(u => new { u.Id, u.BranchId, u.JobTitle, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        var branchRows = await db.Branches.AsNoTracking()
            .Where(b => b.AreaCode != null && b.AreaCode != "")
            .Select(b => new { b.Code, b.AreaCode })
            .ToListAsync(cancellationToken);

        var areaBranches = branchRows
            .GroupBy(b => b.AreaCode!)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Code).ToList());

        var loanArea = await db.Branches.AsNoTracking()
            .Where(b => b.Code == loan.BranchId)
            .Select(b => b.AreaCode)
            .FirstOrDefaultAsync(cancellationToken);

        var loadRows = await db.LoanApplications.AsNoTracking()
            .Where(l => l.Status == LoanStatus.ForApproval && l.AssignedToUserId != null)
            .Select(l => l.AssignedToUserId)
            .ToListAsync(cancellationToken);

        var loads = loadRows
            .Where(id => id != null)
            .GroupBy(id => id!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new List<(Guid Id, string Name, string? JobTitle, AuthorityLevel Level, int Load)>();
        foreach (var u in users)
        {
            var level = AuthorityJobTitles.Map(u.JobTitle);
            if (level is null || (int)level.Value < (int)required)
            {
                continue;
            }

            if (!InScope(level.Value, u.BranchId, loan.BranchId, loanArea, areaBranches))
            {
                continue;
            }

            var name = $"{u.FirstName} {u.LastName}".Trim();
            result.Add((u.Id, name, u.JobTitle, level.Value, loads.GetValueOrDefault(u.Id)));
        }

        return result;
    }

    public static async Task<IReadOnlyList<string>> GetSharedPoolNamesAsync(
        AlasDbContext db,
        CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Approver && u.Status == UserStatus.Active && u.IsAvailable)
            .Select(u => new { u.JobTitle, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        return users
            .Where(u => AuthorityJobTitles.Map(u.JobTitle) == AuthorityLevel.RbgProductCredit)
            .Select(u => $"{u.FirstName} {u.LastName}".Trim())
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<string?> GetSharedPoolDisplayAsync(
        AlasDbContext db,
        LoanApplication loan,
        CancellationToken cancellationToken)
    {
        if (loan.Status != LoanStatus.ForApproval || loan.AssignedToUserId is not null)
        {
            return null;
        }

        var names = await GetSharedPoolNamesAsync(db, cancellationToken);
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static bool InScope(
        AuthorityLevel level,
        string userBranch,
        string loanBranch,
        string? loanArea,
        Dictionary<string, List<string>> areaBranches) => level switch
    {
        AuthorityLevel.BranchHead => string.Equals(userBranch, loanBranch, StringComparison.Ordinal),
        AuthorityLevel.AreaHead => loanArea is not null
            && areaBranches.TryGetValue(loanArea, out var branches)
            && branches.Contains(userBranch),
        AuthorityLevel.RbgProductCredit => true,
        AuthorityLevel.PresidentCoo => true,
        AuthorityLevel.CreComBod => true,
        _ => false
    };
}
