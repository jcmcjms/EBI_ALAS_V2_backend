using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth.Login;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Features.Auth.Refresh;

public sealed class RefreshHandler(
    AlasDbContext db,
    JwtTokenService tokenService,
    TokenStore tokenStore,
    IOptions<ApiOptions> options,
    TimeProvider timeProvider)
{
    public async Task<RefreshOutcome> HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return RefreshOutcome.InvalidToken();
        }

        var existing = await tokenStore.FindUsableAsync(request.RefreshToken, cancellationToken);
        if (existing is null)
        {
            return RefreshOutcome.InvalidToken();
        }

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == existing.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return RefreshOutcome.InvalidToken();
        }

        var jwt = options.Value.Jwt;
        var now = timeProvider.GetUtcNow();
        var (newToken, rawRefresh) = await tokenStore.IssueRefreshTokenAsync(
            user.Id,
            TimeSpan.FromDays(jwt.RefreshTokenDays),
            TimeSpan.FromDays(jwt.RefreshTokenAbsoluteDays),
            cancellationToken);

        await tokenStore.RevokeAsync(existing, newToken.Id, cancellationToken);

        var jti = Guid.NewGuid().ToString("N");
        var access = tokenService.CreateAccessToken(user, jti, now);
        return new RefreshOutcome.Success(
            new LoginResponse(access.Token, rawRefresh, access.ExpiresAt, user.MustChangePassword));
    }
}
