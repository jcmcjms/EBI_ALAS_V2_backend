using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Branches;

public sealed class ListBranchesHandler(AlasDbContext db)
{
    public async Task<IReadOnlyList<BranchResponse>> HandleAsync(
        string? status,
        CancellationToken cancellationToken)
    {
        var query = db.Branches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                // no filter
            }
            else if (Enum.TryParse<BranchStatus>(status, ignoreCase: true, out var parsed))
            {
                query = query.Where(b => b.Status == parsed);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(status), "Status must be Active, Closed, Merged, or all.");
            }
        }

        var rows = await query.OrderBy(b => b.Code).ToListAsync(cancellationToken);
        return rows.Select(b => b.ToResponse()).ToList();
    }
}
