using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.ForcePasswordReset;

public sealed class ForcePasswordResetHandler(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<UserResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id.ToString());

        user.ForcePasswordReset(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return UserMapping.ToResponse(user);
    }
}
