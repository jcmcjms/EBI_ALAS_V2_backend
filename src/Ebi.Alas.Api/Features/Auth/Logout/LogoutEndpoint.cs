using System.Security.Claims;
using Ebi.Alas.Api.Features.Auth.Refresh;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Ebi.Alas.Api.Features.Auth.Logout;

public static class LogoutEndpoint
{
    public static void MapLogout(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/logout", async (
            LogoutRequest? request,
            TokenStore tokenStore,
            ClaimsPrincipal user,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var raw = !string.IsNullOrWhiteSpace(request?.RefreshToken)
                ? request.RefreshToken
                : RefreshCookie.Read(httpContext);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                var existing = await tokenStore.FindUsableAsync(raw, cancellationToken);
                if (existing is not null)
                {
                    await tokenStore.RevokeAsync(existing, Guid.Empty, cancellationToken);
                }
            }

            RefreshCookie.Delete(httpContext);

            var jti = user.FindFirstValue("jti");
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            if (!string.IsNullOrWhiteSpace(jti) && Guid.TryParse(sub, out var userId))
            {
                var expires = await httpContext.GetTokenAsync("expires_at") is { } exp
                    && DateTimeOffset.TryParse(exp, out var parsed)
                    ? parsed
                    : DateTimeOffset.UtcNow.AddMinutes(15);
                await tokenStore.RevokeJtiAsync(jti, userId, expires, cancellationToken);
            }

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("Logout")
        .WithTags("Auth");
    }
}

public sealed record LogoutRequest
{
    public string? RefreshToken { get; init; }
}
