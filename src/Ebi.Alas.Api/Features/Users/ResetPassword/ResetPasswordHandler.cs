using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.ForgotPassword;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.ResetPassword;

public sealed record ResetPasswordResponse(
    string Username,
    string TemporaryPassword,
    bool MustChangePassword);

public sealed class ResetPasswordHandler(
    AlasDbContext db,
    PasswordHasher passwordHasher,
    TimeProvider timeProvider)
{
    public async Task<ResetPasswordResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id.ToString());

        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var now = timeProvider.GetUtcNow();

        user.SetPasswordHash(
            passwordHasher.Hash(temporaryPassword),
            mustChangePassword: true,
            now: now);

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == user.Id)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now, Guid.Empty);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ResetPasswordResponse(user.UserName, temporaryPassword, true);
    }
}
