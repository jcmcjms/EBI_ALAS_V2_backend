using System.Security.Claims;
namespace EBI.ALAS.Api.Features.Auth;
public interface IJwtTokenService
{
    string GenerateToken(User user);
    (string AccessToken, string XsrfToken) GenerateTokenWithXsrf(User user, int? sessionId = null);
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
    ClaimsPrincipal? ValidateToken(string token);
}
