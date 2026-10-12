using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class ListLoanProductsHandler(AlasDbContext db)
{
    public async Task<PageResult<LoanProductResponse>> HandleAsync(
        PageRequest page,
        int maxPageSize,
        int defaultPageSize,
        CancellationToken cancellationToken)
    {
        var normalized = page.Normalize(maxPageSize, defaultPageSize);
        var query = db.LoanProducts.AsNoTracking().OrderBy(p => p.Name);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip(normalized.Skip)
            .Take(normalized.PageSize)
            .ToListAsync(cancellationToken);

        List<LoanProductResponse> items = rows.Count == 0
            ? []
            : [.. rows.Select(p => p.ToResponse())];

        return new PageResult<LoanProductResponse>(items, total, normalized.Page, normalized.PageSize);
    }
}
