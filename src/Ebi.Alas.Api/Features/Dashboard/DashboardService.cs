using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Dashboard;

public sealed record DashboardOverview(
    int PendingRecommendation,
    int PendingEvaluation,
    int PendingApproval,
    int ApprovedToday,
    int Pushbacks);

public sealed class DashboardService(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<DashboardOverview> GetOverviewAsync(CallerContext caller, CancellationToken cancellationToken)
    {
        var today = timeProvider.GetUtcNow().Date;
        var query = db.LoanApplications.AsQueryable();
        if (!caller.CanAccessAllBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        var counts = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                PendingRecommendation = g.Count(l => l.Status == LoanStatus.ForRecommendation),
                PendingEvaluation = g.Count(l => l.Status == LoanStatus.ForChecking),
                PendingApproval = g.Count(l => l.Status == LoanStatus.ForApproval),
                ApprovedToday = g.Count(l => l.Status == LoanStatus.Approved && l.UpdatedAt >= today),
                Pushbacks = g.Count(l => l.Status == LoanStatus.ForRevision),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new DashboardOverview(
            counts?.PendingRecommendation ?? 0,
            counts?.PendingEvaluation ?? 0,
            counts?.PendingApproval ?? 0,
            counts?.ApprovedToday ?? 0,
            counts?.Pushbacks ?? 0);
    }
}
