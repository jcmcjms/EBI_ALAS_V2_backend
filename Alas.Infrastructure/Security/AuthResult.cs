using Alas.Application.Common.Security;

namespace Alas.Infrastructure.Security;

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken,
    int AccessTokenExpirationSeconds,
    int RefreshTokenExpirationDays,
    AuthUserDto User);
