using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for JWT token generation and validation.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generate a short-lived JWT access token for the given user.
    /// </summary>
    string GenerateToken(User user);

    /// <summary>
    /// Generate a short-lived JWT access token alongside a per-session CSRF token.
    /// The returned CSRF token is mirrored as an <c>XsrfToken</c> claim inside
    /// the JWT and must be sent to the client (e.g. as a non-HttpOnly cookie)
    /// so the CSRF middleware can compare it against the <c>X-XSRF-TOKEN</c>
    /// header on subsequent state-changing requests.
    /// </summary>
    /// <returns>
    /// A tuple of (signed access token, csrf token). The caller is responsible
    /// for setting the CSRF token in a non-HttpOnly cookie.
    /// </returns>
    (string AccessToken, string XsrfToken) GenerateTokenWithXsrf(User user);

    /// <summary>
    /// Generate a cryptographically secure random refresh token string.
    /// The raw token is returned once; only its hash should be stored.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Compute the SHA-256 hash of a refresh token for secure storage.
    /// </summary>
    string HashRefreshToken(string refreshToken);

    /// <summary>
    /// Validate a JWT token and return its claims principal.
    /// </summary>
    ClaimsPrincipal? ValidateToken(string token);
}
