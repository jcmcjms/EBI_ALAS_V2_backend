using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth.Login;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Features.Auth.Login;

public sealed class LoginHandler(
    AlasDbContext db,
    PasswordHasher passwordHasher,
    JwtTokenService tokenService,
    TokenStore tokenStore,
    IOptions<ApiOptions> options,
    TimeProvider timeProvider)
{
    public async Task<LoginOutcome> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName == request.UserName.Trim(), cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return LoginOutcome.InvalidCredentials();
        }

        if (user.Status != UserStatus.Active)
        {
            return LoginOutcome.Suspended();
        }

        var jwt = options.Value.Jwt;
        var now = timeProvider.GetUtcNow();
        var jti = Guid.NewGuid().ToString("N");
        var access = tokenService.CreateAccessToken(user, jti, now);
        var (_, rawRefresh) = await tokenStore.IssueRefreshTokenAsync(
            user.Id,
            TimeSpan.FromDays(jwt.RefreshTokenDays),
            TimeSpan.FromDays(jwt.RefreshTokenAbsoluteDays),
            cancellationToken);

        return new LoginOutcome.Success(new LoginResponse(
            access.Token,
            rawRefresh,
            access.ExpiresAt,
            user.MustChangePassword));
    }
}
