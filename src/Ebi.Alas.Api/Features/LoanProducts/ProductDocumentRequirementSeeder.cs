using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class ProductDocumentRequirementSeeder(AlasDbContext db)
{
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.LoanProductDocumentRequirements.AsNoTracking().AnyAsync(cancellationToken))
        {
            return false;
        }

        foreach (var (productCode, code) in ProductDocumentRequirementCatalog.Defaults)
        {
            db.LoanProductDocumentRequirements.Add(
                LoanProductDocumentRequirement.Create(productCode, code, code));
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
