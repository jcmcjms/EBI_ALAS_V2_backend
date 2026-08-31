using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for JWT token generation and validation.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(User user);

    /// <summary>
    /// Generates a JWT access token alongside a per-session CSRF token. The CSRF
    /// token is mirrored as an <c>XsrfToken</c> claim inside the JWT; the caller
    /// is responsible for setting it as a non-HttpOnly cookie so the CSRF
    /// middleware can compare it against the <c>X-XSRF-TOKEN</c> header on
    /// subsequent state-changing requests.
    /// </summary>
    (string AccessToken, string XsrfToken) GenerateTokenWithXsrf(User user);

    // The raw token is returned once; only its hash should be stored.
    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);

    ClaimsPrincipal? ValidateToken(string token);
}
