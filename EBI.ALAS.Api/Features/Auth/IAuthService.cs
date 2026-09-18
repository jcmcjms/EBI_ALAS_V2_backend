using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, HttpContext http, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(HttpContext http, CancellationToken ct = default);
    Task LogoutAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken ct = default);
    Task ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request, HttpContext http, CancellationToken ct = default);
}

public class AuthResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public LoginResponse? Response { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public string? XsrfToken { get; set; }
    public DateTime? AccessTokenExpiry { get; set; }

    public static AuthResult SuccessResult(LoginResponse response, string refreshToken, DateTime refreshTokenExpiry, string xsrfToken, DateTime accessTokenExpiry) =>
        new()
        {
            Success = true,
            Response = response,
            RefreshToken = refreshToken,
            RefreshTokenExpiry = refreshTokenExpiry,
            XsrfToken = xsrfToken,
            AccessTokenExpiry = accessTokenExpiry
        };

    public static AuthResult FailureResult(string errorMessage) =>
        new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}
