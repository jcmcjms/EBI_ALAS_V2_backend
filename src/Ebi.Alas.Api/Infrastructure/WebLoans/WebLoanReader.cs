using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.WebLoans;

public sealed class WebLoanReader(WebLoanDbContext db) : IWebLoanReader
{
    public async Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(
        string keyword,
        int max,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var limit = Math.Clamp(max, 1, 50);
        var term = keyword.Trim();
        return await db.CisInfos.AsNoTracking()
            .Where(c => c.CifNo.Contains(term) || c.FullName.Contains(term))
            .OrderBy(c => c.FullName)
            .Take(limit)
            .Select(c => new CisSearchResult(c.CifNo, c.FullName, c.Address))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(
        int max,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(max, 1, 100);
        return await db.LoanProducts.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.ProductName)
            .Take(limit)
            .Select(p => new LoanProductLookup(p.ProductCode, p.ProductName, p.InterestRatePerMonth))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(
        string cifNo,
        int max,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cifNo);
        var limit = Math.Clamp(max, 1, 50);
        return await db.LoanAccounts.AsNoTracking()
            .Where(a => a.CifNo == cifNo && a.Balance > 0)
            .OrderBy(a => a.AccountNo)
            .Take(limit)
            .Select(a => new OutstandingLoanRow(a.AccountNo, a.Status, a.Balance))
            .ToListAsync(cancellationToken);
    }
}
