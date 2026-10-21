using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.SetAvailability;

public sealed record SetAvailabilityRequest(bool Available);

public sealed class SetUserAvailabilityHandler(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<UserResponse> HandleAsync(
        Guid id,
        bool available,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id.ToString());

        user.SetAvailable(available, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return UserMapping.ToResponse(user);
    }
}
