using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Branches;

public sealed class CreateBranchHandler(AlasDbContext db)
{
    public async Task<BranchResponse> HandleAsync(
        CreateBranchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AreaCode);

        var code = request.Code.Trim();
        if (await db.Branches.AnyAsync(b => b.Code == code, cancellationToken))
        {
            throw new InvalidOperationException($"Branch code '{code}' already exists.");
        }

        var branch = Branch.Create(code, request.Name, request.AreaCode, DateTimeOffset.UtcNow);
        db.Branches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);
        return branch.ToResponse();
    }
}
