using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Dashboard;

public sealed record FlaggedDocumentItem(
    Guid Id,
    string LamId,
    string BranchId,
    DateTimeOffset WaitingSinceUtc,
    int MissingCount,
    string ClientName,
    string EncoderName,
    string? FlaggedByName,
    DateTimeOffset? FlaggedAt);

public sealed record DashboardOverview(
    int PendingRecommendation,
    int PendingEvaluation,
    int PendingApproval,
    int ApprovedToday,
    int Pushbacks,
    IReadOnlyList<FlaggedDocumentItem> FlaggedDocuments);

public sealed class DashboardService(AlasDbContext db, TimeProvider timeProvider)
{
    public const int FlaggedDocumentsCap = 20;

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

        var flaggedQuery = db.LoanApplications.AsNoTracking();
        if (!caller.CanAccessAllBranches)
        {
            flaggedQuery = flaggedQuery.Where(l => l.BranchId == caller.BranchId);
        }

        var flaggedRows = await flaggedQuery
            .Where(l => l.DocumentsFlaggedAt != null)
            .OrderBy(l => l.DocumentsFlaggedAt)
            .Take(FlaggedDocumentsCap)
            .Select(l => new
            {
                l.Id,
                l.LamId,
                l.BranchId,
                l.ClientName,
                l.DocumentFlagMissingCount,
                l.DocumentsFlaggedAt,
                l.DocumentsFlaggedById,
                l.CreatedBy,
                l.LastActionDate,
            })
            .ToListAsync(cancellationToken);

        var userIds = flaggedRows
            .SelectMany(r => new[] { r.CreatedBy, r.DocumentsFlaggedById ?? r.CreatedBy })
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => (u.FirstName + " " + u.LastName).Trim(),
                cancellationToken);

        var flagged = flaggedRows
            .Select(r =>
            {
                names.TryGetValue(r.CreatedBy, out var encoderName);
                string? flaggedByName = null;
                if (r.DocumentsFlaggedById is { } flaggerId && names.TryGetValue(flaggerId, out var name))
                {
                    flaggedByName = name;
                }

                return new FlaggedDocumentItem(
                    r.Id,
                    r.LamId,
                    r.BranchId,
                    r.DocumentsFlaggedAt ?? r.LastActionDate,
                    r.DocumentFlagMissingCount,
                    r.ClientName,
                    encoderName ?? string.Empty,
                    flaggedByName,
                    r.DocumentsFlaggedAt);
            })
            .ToList();

        return new DashboardOverview(
            counts?.PendingRecommendation ?? 0,
            counts?.PendingEvaluation ?? 0,
            counts?.PendingApproval ?? 0,
            counts?.ApprovedToday ?? 0,
            counts?.Pushbacks ?? 0,
            flagged);
    }
}
