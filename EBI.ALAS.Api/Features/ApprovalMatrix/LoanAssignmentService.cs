using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public interface ILoanAssignmentService
{
    Task AssignAsync(LoanApplication loan, CancellationToken ct = default);
    Task ReleaseAsync(int loanId, int actorUserId, CancellationToken ct = default);
    Task TryAssignPendingForAsync(int userId, CancellationToken ct = default);
    Task<bool> IsReviewingAsync(int userId, CancellationToken ct = default);
}

public sealed class LoanAssignmentService : ILoanAssignmentService
{
    private readonly AppDbContext _db;
    private readonly IPresenceService _presence;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly Common.Time.ITimeProvider _time;

    public LoanAssignmentService(
        AppDbContext db,
        IPresenceService presence,
        IHubContext<NotificationHub> hub,
        Common.Time.ITimeProvider time)
    {
        _db = db;
        _presence = presence;
        _hub = hub;
        _time = time;
    }

    public async Task AssignAsync(LoanApplication loan, CancellationToken ct = default)
    {
        if (loan.RequiredApprovalTier is not { } requiredTier) return;

        var tier = requiredTier;
        var candidates = await CandidatesAsync(loan, tier, ct);

        while (candidates.Count == 0 && tier < 5) // max 5 tiers in the matrix
        {
            tier++;
            candidates = await CandidatesAsync(loan, tier, ct);
        }

        if (candidates.Count == 0) return; // no approvers in any tier

        // Active leases per candidate (one loan in ForApproval = "reviewing").
        var ids = candidates.Select(c => c.User.Id).ToList();
        var leases = await _db.LoanApplications.AsNoTracking()
            .Where(l => l.Status == "ForApproval" && l.AssignedApproverId != null && ids.Contains(l.AssignedApproverId.Value))
            .GroupBy(l => l.AssignedApproverId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var pick = candidates
            .OrderByDescending(c => _presence.IsOnline(c.User.Id) && !(leases.GetValueOrDefault(c.User.Id) > 0)) // online + idle
            .ThenByDescending(c => _presence.IsOnline(c.User.Id))                                                // online
            .ThenByDescending(c => !(leases.GetValueOrDefault(c.User.Id) > 0))                                   // idle
            .ThenBy(c => c.Authority.Priority)                                                                   // BH -> OIC1 ...
            .ThenBy(c => leases.GetValueOrDefault(c.User.Id))
            .First();

        loan.AssignedApproverId = pick.User.Id;
        loan.AssignedAt = _time.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _hub.Clients.User(pick.User.Id.ToString())
            .SendAsync("LoanAssigned", new { loan.Id, loan.LamId, loan.Status, escalated = tier != requiredTier });
        await _hub.Clients.Group("Approvers").SendAsync("LoanAssigned", new { loan.Id, loan.LamId, loan.Status });
    }

    private async Task<List<(User User, ApprovalAuthority Authority)>> CandidatesAsync(
        LoanApplication loan, int tier, CancellationToken ct)
    {
        var tierKeys = await _db.ApprovalAuthorities.AsNoTracking()
            .Where(a => a.Tier == tier).Select(a => a.Key).ToListAsync(ct);

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Approver && u.IsActive
                        && u.ApprovalAuthorityKey != null && tierKeys.Contains(u.ApprovalAuthorityKey))
            .ToListAsync(ct);

        var coverage = await _db.UserBranchCoverages.AsNoTracking()
            .Where(ubc => users.Select(u => u.Id).Contains(ubc.UserId))
            .GroupBy(ubc => ubc.UserId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Select(ubc => ubc.BranchCode).ToHashSet(),
                ct);

        var areaBranches = loan.BranchCode is not null
            ? await _db.Branches.AsNoTracking().Where(b => b.AreaCode != null)
                .GroupBy(b => b.AreaCode!).ToDictionaryAsync(g => g.Key, g => g.Select(b => b.Code).ToList(), ct)
            : [];

        var result = new List<(User User, ApprovalAuthority Authority)>();
        foreach (var u in users)
        {
            var auth = await _db.ApprovalAuthorities.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Key == u.ApprovalAuthorityKey, ct);
            if (auth is null) continue;

            // For Branch scope: use multi-branch coverage if available,
            // fall back to the user's single BranchId.
            var coveredBranches = coverage.GetValueOrDefault(u.Id)
                ?? new HashSet<string> { u.BranchId };

            if (InScope(auth.ScopeType, u.BranchId, loan.BranchCode, coveredBranches, areaBranches))
                result.Add((u, auth));
        }
        return result;
    }

    private static bool InScope(AuthorityScope scope, string userBranch, string? loanBranch,
        HashSet<string> coveredBranches, Dictionary<string, List<string>> areaBranches) => scope switch
    {
        AuthorityScope.Global => true,
        AuthorityScope.Branch => coveredBranches.Contains(loanBranch ?? ""),
        AuthorityScope.Area => areaBranches.Values.Any(set => set.Contains(userBranch) && set.Contains(loanBranch ?? "")),
        _ => false,
    };

    public async Task<bool> IsReviewingAsync(int userId, CancellationToken ct = default) =>
        await _db.LoanApplications.AnyAsync(l => l.AssignedApproverId == userId && l.Status == "ForApproval", ct);

    public async Task ReleaseAsync(int loanId, int actorUserId, CancellationToken ct = default)
    {
        var loan = await _db.LoanApplications.FindAsync([loanId], ct);
        if (loan is null || loan.Status != "ForApproval") return;
        if (loan.AssignedApproverId != actorUserId) return;
        loan.AssignedApproverId = null;
        loan.AssignedAt = null;
        await _db.SaveChangesAsync(ct);
        await _hub.Clients.Group("Approvers").SendAsync("LoanAssigned", new { loan.Id, loan.LamId, loan.Status });
    }

    public async Task TryAssignPendingForAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.ApprovalAuthorityKey is null || await IsReviewingAsync(userId, ct)) return;

        var auth = await _db.ApprovalAuthorities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == user.ApprovalAuthorityKey, ct);
        if (auth is null) return;

        var coverage = new HashSet<string>(
            await _db.UserBranchCoverages.AsNoTracking()
                .Where(ubc => ubc.UserId == userId)
                .Select(ubc => ubc.BranchCode)
                .ToListAsync(ct));

        // Accept loans at or below my tier (tier escalation: a higher-tier
        // approver can serve lower-tier loans when no one else is available).
        var pending = await _db.LoanApplications
            .Where(l => l.Status == "ForApproval" && l.AssignedApproverId == null
                        && l.RequiredApprovalTier <= auth.Tier)
            .OrderBy(l => l.LastActionDate).Take(5).ToListAsync(ct);

        foreach (var loan in pending.Where(l => InScope(auth.ScopeType, user.BranchId, l.BranchCode, coverage, [])))
            await AssignAsync(loan, ct);
    }
}
