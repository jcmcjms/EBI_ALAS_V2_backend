using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Auth;
public interface IJwtTokenService
{
    string GenerateToken(User user);
    (string AccessToken, string XsrfToken) GenerateTokenWithXsrf(User user);

    // The raw token is returned once; only its hash should be stored.
    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);

    ClaimsPrincipal? ValidateToken(string token);
}
