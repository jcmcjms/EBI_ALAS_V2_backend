using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Branches;

public sealed class UpdateBranchHandler(AlasDbContext db)
{
    public async Task<BranchResponse> HandleAsync(
        string code,
        UpdateBranchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Code == code, cancellationToken)
            ?? throw new NotFoundException("Branch", code);

        branch.UpdateDetails(request.Name, request.AreaCode, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return branch.ToResponse();
    }
}
