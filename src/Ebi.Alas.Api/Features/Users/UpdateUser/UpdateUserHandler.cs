using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.UpdateUser;

public sealed class UpdateUserHandler(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<UserResponse> HandleAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ValidationException("Email is required for password reset.");
        }

        if (request.Email.Trim().Length > 200)
        {
            throw new ValidationException("Email must not exceed 200 characters.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id.ToString());
        user.UpdateProfile(
            request.FirstName,
            request.LastName,
            request.MiddleName,
            request.Email,
            request.BranchId,
            request.Role,
            timeProvider.GetUtcNow(),
            request.JobTitle);
        await db.SaveChangesAsync(cancellationToken);
        return UserMapping.ToResponse(user);
    }
}