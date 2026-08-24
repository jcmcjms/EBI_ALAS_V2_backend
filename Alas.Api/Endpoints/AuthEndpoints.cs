using System.Security.Claims;
using Alas.Api.Validation;
using Alas.Application.Common.Security;
using Alas.Infrastructure.Security;

namespace Alas.Api.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshTokenCookieName = "alas.refresh_token";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .Produces<AuthUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress(httpContext);
        var userAgent = GetUserAgent(httpContext);

        var result = await authService.LoginAsync(
            request,
            ipAddress,
            userAgent,
            cancellationToken);

        if (result is null)
        {
            return Results.Unauthorized();
        }

        SetRefreshTokenCookie(
            httpContext,
            result.RefreshToken,
            result.RefreshTokenExpirationDays);

        return Results.Ok(new AuthResponse(
            result.AccessToken,
            result.AccessTokenExpirationSeconds,
            result.User));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var refreshToken = httpContext.Request.Cookies[RefreshTokenCookieName];

        // Temporary compatibility fallback.
        // The secure path is cookie-only. Remove body support after frontend migration.
        if (string.IsNullOrWhiteSpace(refreshToken) &&
            httpContext.Request.HasJsonContentType() &&
            httpContext.Request.ContentLength > 0)
        {
            var body = await httpContext.Request.ReadFromJsonAsync<RefreshRequest>(cancellationToken);
            refreshToken = body?.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Results.Unauthorized();
        }

        var ipAddress = GetClientIpAddress(httpContext);
        var userAgent = GetUserAgent(httpContext);

        var result = await authService.RefreshAsync(
            refreshToken,
            ipAddress,
            userAgent,
            cancellationToken);

        if (result is null)
        {
            DeleteRefreshTokenCookie(httpContext);
            return Results.Unauthorized();
        }

        SetRefreshTokenCookie(
            httpContext,
            result.RefreshToken,
            result.RefreshTokenExpirationDays);

        return Results.Ok(new AuthResponse(
            result.AccessToken,
            result.AccessTokenExpirationSeconds,
            result.User));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var refreshToken = httpContext.Request.Cookies[RefreshTokenCookieName];

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await authService.LogoutAsync(refreshToken, cancellationToken);
        }

        DeleteRefreshTokenCookie(httpContext);

        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await authService.GetSessionAsync(userId, cancellationToken);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(user);
    }

    private static string GetClientIpAddress(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string GetUserAgent(HttpContext httpContext)
    {
        return httpContext.Request.Headers.UserAgent.ToString();
    }

    private static void SetRefreshTokenCookie(
        HttpContext httpContext,
        string refreshToken,
        int expirationDays)
    {
        var options = BuildRefreshTokenCookieOptions();

        options.Expires = DateTimeOffset.UtcNow.AddDays(expirationDays);

        httpContext.Response.Cookies.Append(
            RefreshTokenCookieName,
            refreshToken,
            options);
    }

    private static void DeleteRefreshTokenCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            RefreshTokenCookieName,
            BuildRefreshTokenCookieOptions());
    }

    private static CookieOptions BuildRefreshTokenCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            IsEssential = true
        };
    }
}
