using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Auth.ChangePassword;

public sealed class ChangePasswordHandler(
    AlasDbContext db,
    PasswordHasher passwordHasher,
    TimeProvider timeProvider,
    ILogger<ChangePasswordHandler> logger)
{
    public async Task<ChangePasswordOutcome> HandleAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CurrentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NewPassword);

        if (request.CurrentPassword.Length > ChangePasswordRequest.MaxPasswordLength
            || request.NewPassword.Length > ChangePasswordRequest.MaxPasswordLength)
        {
            return ChangePasswordOutcome.InvalidNewPassword(
                $"Password must be at most {ChangePasswordRequest.MaxPasswordLength} characters.");
        }

        if (request.NewPassword.Length < ChangePasswordRequest.MinPasswordLength)
        {
            return ChangePasswordOutcome.InvalidNewPassword(
                $"Password must be at least {ChangePasswordRequest.MinPasswordLength} characters.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return ChangePasswordOutcome.UserNotFound();
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return ChangePasswordOutcome.InvalidCurrentPassword();
        }

        user.SetPasswordHash(
            passwordHasher.Hash(request.NewPassword),
            mustChangePassword: false,
            now: timeProvider.GetUtcNow());

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(timeProvider.GetUtcNow(), Guid.Empty);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("{Event} userId={UserId}", AuthEvents.PasswordChanged, userId);
        return new ChangePasswordOutcome.Success();
    }
}
