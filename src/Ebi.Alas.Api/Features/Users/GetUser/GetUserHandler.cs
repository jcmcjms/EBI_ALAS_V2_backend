using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.GetUser;

public sealed class GetUserHandler(AlasDbContext db)
{
    public async Task<UserResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(User), id.ToString());
        return UserMapping.ToResponse(user);
    }
}
