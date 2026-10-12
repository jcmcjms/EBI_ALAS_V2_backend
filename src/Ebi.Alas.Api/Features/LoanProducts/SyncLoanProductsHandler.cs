using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class SyncLoanProductsHandler(
    AlasDbContext db,
    IWebLoanReader webLoans,
    TimeProvider timeProvider)
{
    public const int MaxCatalogProducts = 500;

    public async Task<LoanProductSyncResult> HandleAsync(CancellationToken cancellationToken)
    {
        var syncedAt = timeProvider.GetUtcNow();
        var catalog = await webLoans.GetAllProductsAsync(MaxCatalogProducts, cancellationToken);

        // Prefetch existing products once; per-item FirstOrDefault was an N+1.
        var existingByCode = await db.LoanProducts.ToDictionaryAsync(p => p.Code, cancellationToken);

        var added = 0;
        var updated = 0;
        var preserved = 0;

        foreach (var item in catalog)
        {
            if (string.IsNullOrWhiteSpace(item.ProductCode))
            {
                continue;
            }

            var code = item.ProductCode.Trim();
            if (!existingByCode.TryGetValue(code, out var existing))
            {
                var product = LoanProduct.Create(
                    code,
                    item.ProductName,
                    item.InterestRatePerMonth,
                    minTermDays: 1,
                    maxTermDays: 1,
                    syncedAt);
                product.SetRetired(item.IsRetired);
                db.LoanProducts.Add(product);
                added++;
                continue;
            }

            var catalogChanged = existing.ApplyCatalog(
                item.ProductName,
                existing.InterestRatePerMonth,
                syncedAt);
            var wasRetired = !existing.IsActive;
            existing.SetRetired(item.IsRetired);
            if (catalogChanged || wasRetired != item.IsRetired)
            {
                updated++;
            }
            else
            {
                preserved++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new LoanProductSyncResult(added, updated, preserved, syncedAt);
    }
}
