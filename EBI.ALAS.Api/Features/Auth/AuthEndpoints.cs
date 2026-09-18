using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Auth;

public static class AuthEndpoints
{
    private const string RefreshTokenCookieName = "refreshToken";
    private const string XsrfCookieName = "XSRF-TOKEN";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IValidator<LoginRequest> validator,
            IAuthService authService,
            HttpContext http) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", errors.SelectMany(e => e.Value).ToList()));
            }

            var result = await authService.LoginAsync(request, http);

            if (!result.Success)
                return Results.Unauthorized();

            SetRefreshTokenCookie(http, result.RefreshToken!, result.RefreshTokenExpiry!.Value);
            SetXsrfCookie(http, result.XsrfToken!, result.AccessTokenExpiry!.Value);

            return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(result.Response!, "Login successful"));
        })
        .WithName("Login")
        .Produces<ApiResponse<LoginResponse>>(200)
        .Produces<ApiResponse>(401)
        .Produces<ApiResponse>(400)
        .RequireRateLimiting("LoginLimiter");

        group.MapPost("/refresh", async (
            HttpContext http,
            IAuthService authService) =>
        {
            var result = await authService.RefreshAsync(http);

            if (!result.Success)
                return Results.Unauthorized();

            SetRefreshTokenCookie(http, result.RefreshToken!, result.RefreshTokenExpiry!.Value);
            SetXsrfCookie(http, result.XsrfToken!, result.AccessTokenExpiry!.Value);

            return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(result.Response!, "Token refreshed successfully"));
        })
        .WithName("RefreshToken")
        .Produces<ApiResponse<LoginResponse>>(200)
        .Produces<ApiResponse>(401);

        group.MapPost("/logout", async (
            System.Security.Claims.ClaimsPrincipal principal,
            HttpContext http,
            IAuthService authService) =>
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
        })
        .WithName("Logout")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .RequireAuthorization();

        group.MapPost("/change-password", async (
            System.Security.Claims.ClaimsPrincipal principal,
            [FromBody] ChangePasswordRequest request,
            IValidator<ChangePasswordRequest> validator,
            IAuthService authService,
            HttpContext http) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", errors.SelectMany(e => e.Value).ToList()));
            }

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
                return Results.Unauthorized();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(ex.Message));
            }
        })
        .WithName("ChangePassword")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(401)
        .RequireAuthorization();
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

        if (http.Request.Host.Host == "localhost" || http.Request.Host.Host == "127.0.0.1")
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

        if (http.Request.Host.Host == "localhost" || http.Request.Host.Host == "127.0.0.1")
            cookieOptions.Secure = false;

        http.Response.Cookies.Append(XsrfCookieName, xsrfToken, cookieOptions);
    }
}
