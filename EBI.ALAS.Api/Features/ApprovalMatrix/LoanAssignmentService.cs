using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
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
        if (loan.RequiredApprovalTier is not { } tier) return;

        var candidates = await CandidatesAsync(loan, tier, ct);
        if (candidates.Count == 0) return;   // tier queue until an approver exists/logs in

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
            .SendAsync("LoanAssigned", new { loan.Id, loan.LamId, loan.Status });
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

            if (InScope(auth.ScopeType, u.BranchId, loan.BranchCode, areaBranches))
                result.Add((u, auth));
        }
        return result;
    }

    private static bool InScope(AuthorityScope scope, string userBranch, string? loanBranch,
        Dictionary<string, List<string>> areaBranches) => scope switch
    {
        AuthorityScope.Global => true,
        AuthorityScope.Branch => string.Equals(userBranch, loanBranch, StringComparison.Ordinal),
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

        var pending = await _db.LoanApplications
            .Where(l => l.Status == "ForApproval" && l.AssignedApproverId == null
                        && l.RequiredApprovalTier == auth.Tier)
            .OrderBy(l => l.LastActionDate).Take(1).ToListAsync(ct);

        foreach (var loan in pending.Where(l => InScope(auth.ScopeType, user.BranchId, l.BranchCode, [])))
            await AssignAsync(loan, ct);
    }
}
