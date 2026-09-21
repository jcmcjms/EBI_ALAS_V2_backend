using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Authentication endpoints: login, refresh, logout, change password.
/// Follows Clean Code: small functions, early returns, no nested conditionals.
/// </summary>
public static class AuthEndpoints
{
    private const string RefreshTokenCookieName = "refreshToken";
    private const string XsrfCookieName = "XSRF-TOKEN";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", HandleLogin)
            .WithName("Login")
            .Produces<ApiResponse<LoginResponse>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(400)
            .RequireRateLimiting("LoginLimiter");

        group.MapPost("/refresh", HandleRefresh)
            .WithName("RefreshToken")
            .Produces<ApiResponse<LoginResponse>>(200)
            .Produces<ApiResponse>(401);

        group.MapPost("/logout", HandleLogout)
            .WithName("Logout")
            .Produces<ApiResponse>(200)
            .Produces<ApiResponse>(400)
            .RequireAuthorization();

        group.MapPost("/change-password", HandleChangePassword)
            .WithName("ChangePassword")
            .Produces<ApiResponse>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleLogin(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        IAuthService authService,
        HttpContext http)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", validationResult.Errors.Select(e => e.ErrorMessage).ToList()));

        var result = await authService.LoginAsync(request, http);

        if (!result.Success)
            // Generic on purpose: never reveal whether the username exists or the
            // account is suspended (user-enumeration). Specifics stay in Serilog.
            return Results.Json(
                ApiResponse.ErrorResponse("Invalid username or password."),
                statusCode: StatusCodes.Status401Unauthorized);

        SetRefreshTokenCookie(http, result.RefreshToken!, result.RefreshTokenExpiry!.Value);
        SetXsrfCookie(http, result.XsrfToken!, result.AccessTokenExpiry!.Value);

        return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(result.Response!, "Login successful"));
    }

    private static async Task<IResult> HandleRefresh(
        HttpContext http,
        IAuthService authService)
    {
        var result = await authService.RefreshAsync(http);

        if (!result.Success)
            return Results.Json(
                ApiResponse.ErrorResponse("Session expired or invalid. Please log in again."),
                statusCode: StatusCodes.Status401Unauthorized);

        SetRefreshTokenCookie(http, result.RefreshToken!, result.RefreshTokenExpiry!.Value);
        SetXsrfCookie(http, result.XsrfToken!, result.AccessTokenExpiry!.Value);

        return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(result.Response!, "Token refreshed successfully"));
    }

    private static async Task<IResult> HandleLogout(
        ClaimsPrincipal principal,
        HttpContext http,
        IAuthService authService)
    {
        await authService.LogoutAsync(principal, http);

        http.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth"
        });

        return Results.Ok(ApiResponse.SuccessResponse("Logged out successfully"));
    }

    private static async Task<IResult> HandleChangePassword(
        ClaimsPrincipal principal,
        ChangePasswordRequest request,
        IValidator<ChangePasswordRequest> validator,
        IAuthService authService,
        HttpContext http)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", validationResult.Errors.Select(e => e.ErrorMessage).ToList()));

        try
        {
            await authService.ChangePasswordAsync(principal, request, http);

            http.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth"
            });

            return Results.Ok(ApiResponse.SuccessResponse("Password changed successfully. Please log in again."));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(
                ApiResponse.ErrorResponse("Session expired or invalid. Please log in again."),
                statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ApiResponse.ErrorResponse(ex.Message));
        }
    }

    private static void SetRefreshTokenCookie(HttpContext http, string refreshToken, DateTime expiry)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expiry,
            Path = "/api/auth",
            IsEssential = true
        };

        if (http.Request.IsLocalRequest())
            cookieOptions.Secure = false;

        http.Response.Cookies.Append(RefreshTokenCookieName, refreshToken, cookieOptions);
    }

    private static void SetXsrfCookie(HttpContext http, string xsrfToken, DateTime expiry)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expiry,
            Path = "/",
            IsEssential = true
        };

        if (http.Request.IsLocalRequest())
            cookieOptions.Secure = false;

        http.Response.Cookies.Append(XsrfCookieName, xsrfToken, cookieOptions);
    }
}

/// <summary>
/// Extension methods for HttpRequest to reduce duplication.
/// </summary>
internal static class HttpRequestExtensions
{
    /// <summary>
    /// Returns true if the request is from localhost (development environment).
    /// </summary>
    public static bool IsLocalRequest(this HttpRequest request)
        => request.Host.Host is "localhost" or "127.0.0.1";
}
