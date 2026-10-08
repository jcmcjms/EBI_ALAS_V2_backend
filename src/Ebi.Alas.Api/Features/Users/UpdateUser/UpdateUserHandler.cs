using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.UpdateUser;

public sealed class UpdateUserHandler(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<UserResponse> HandleAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id.ToString());
        user.UpdateProfile(request.FullName, request.Email, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return UserMapping.ToResponse(user);
    }
}
