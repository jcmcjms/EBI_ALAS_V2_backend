using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.CreateUser;

public sealed class CreateUserHandler(
    AlasDbContext db,
    PasswordHasher passwordHasher,
    TimeProvider timeProvider)
{
    public const int MinimumPasswordLength = 12;

    public async Task<CreateUserOutcome> HandleAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BranchId);

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return CreateUserOutcome.InvalidPassword("Password is required.");
        }

        if (request.Password.Length < MinimumPasswordLength)
        {
            return CreateUserOutcome.InvalidPassword($"Password must be at least {MinimumPasswordLength} characters.");
        }

        var userName = request.UserName.Trim();
        if (await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
        {
            return CreateUserOutcome.UsernameTaken(userName);
        }

        var hash = passwordHasher.Hash(request.Password);
        var now = timeProvider.GetUtcNow();
        var user = User.Create(
            userName,
            hash,
            request.FullName,
            request.Email,
            request.BranchId,
            request.Role,
            now);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return new CreateUserOutcome.Success(UserMapping.ToResponse(user));
    }
}
