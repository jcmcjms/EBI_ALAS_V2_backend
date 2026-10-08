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
    public async Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var today = timeProvider.GetUtcNow().Date;
        return new DashboardOverview(
            await db.LoanApplications.CountAsync(l => l.Status == LoanStatus.ForRecommendation, cancellationToken),
            await db.LoanApplications.CountAsync(l => l.Status == LoanStatus.ForChecking, cancellationToken),
            await db.LoanApplications.CountAsync(l => l.Status == LoanStatus.ForApproval, cancellationToken),
            await db.LoanApplications.CountAsync(l => l.Status == LoanStatus.Approved && l.UpdatedAt >= today, cancellationToken),
            await db.LoanApplications.CountAsync(l => l.Status == LoanStatus.ForRevision, cancellationToken));
    }
}
