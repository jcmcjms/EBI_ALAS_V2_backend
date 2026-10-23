using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Approvals;

/// <summary>
/// Approval desks only surface loans whose required authority is the caller's own
/// level (or work already assigned to them). A COO must not see Area Head cases.
/// </summary>
public static class ApproverDeskVisibility
{
    public static async Task<HashSet<Guid>> VisibleLoanIdsAsync(
        AlasDbContext db,
        Guid userId,
        IReadOnlyList<LoanApplication> loans,
        CancellationToken cancellationToken)
    {
        var visible = new HashSet<Guid>();
        if (loans.Count == 0)
        {
            return visible;
        }

        foreach (var loan in loans)
        {
            if (loan.Status != LoanStatus.ForApproval || loan.AssignedToUserId == userId)
            {
                visible.Add(loan.Id);
            }
        }

        var pending = loans
            .Where(l => l.Status == LoanStatus.ForApproval && l.AssignedToUserId != userId)
            .ToList();
        if (pending.Count == 0)
        {
            return visible;
        }

        var jobTitle = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.JobTitle)
            .FirstOrDefaultAsync(cancellationToken);

        var catalogTiers = await db.ApprovalAuthorityUsers.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Join(db.ApprovalAuthorities.AsNoTracking(),
                x => x.ApprovalAuthorityId,
                a => a.Id,
                (x, a) => a.Tier)
            .ToListAsync(cancellationToken);

        var legacyLevel = AuthorityJobTitles.Map(jobTitle);

        var loanIds = pending.Select(l => l.Id).ToList();
        var deviationRows = await db.LoanDeviations.AsNoTracking()
            .Where(d => loanIds.Contains(d.LoanApplicationId))
            .Select(d => new { d.LoanApplicationId, d.Description, d.Severity })
            .ToListAsync(cancellationToken);
        var deviationsByLoan = deviationRows
            .GroupBy(d => d.LoanApplicationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var routing = new ApprovalRoutingService(db);
        foreach (var loan in pending)
        {
            deviationsByLoan.TryGetValue(loan.Id, out var deviations);
            var maxSeverity = deviations is { Count: > 0 } ? deviations.Max(d => d.Severity) : 0;
            var ratePrice = deviations?.Any(d => ApprovalMatrix.IsRatePriceDeviation(d.Description)) == true;

            var catalog = await routing.TryResolveAsync(loan, maxSeverity, ratePrice, cancellationToken);
            if (catalog is not null)
            {
                if (catalog.Candidates.Any(c => c.UserId == userId)
                    || catalogTiers.Contains(catalog.RequiredTier))
                {
                    visible.Add(loan.Id);
                }

                continue;
            }

            var decision = ApprovalMatrix.Resolve(new ApprovalInputs(
                loan.LoanType,
                maxSeverity,
                loan.Principal + loan.TotalInterest,
                ratePrice));

            if (legacyLevel is not { } level)
            {
                continue;
            }

            var sharedPool = new[]
            {
                AuthorityLevel.RbgHead,
                AuthorityLevel.ProductHead,
                AuthorityLevel.CreditHead,
            };
            var required = decision.RequiredLevel;
            if (level == required
                || (sharedPool.Contains(level) && sharedPool.Contains(required)))
            {
                visible.Add(loan.Id);
            }
        }

        return visible;
    }
}
