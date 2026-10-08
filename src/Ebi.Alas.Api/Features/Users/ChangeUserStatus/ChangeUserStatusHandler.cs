using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.ChangeUserStatus;

public sealed class ChangeUserStatusHandler(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<UserResponse> HandleAsync(Guid id, UserStatus status, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id.ToString());
        var now = timeProvider.GetUtcNow();
        if (status == UserStatus.Suspended)
        {
            user.Suspend(now);
        }
        else
        {
            user.Activate(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return UserMapping.ToResponse(user);
    }
}
