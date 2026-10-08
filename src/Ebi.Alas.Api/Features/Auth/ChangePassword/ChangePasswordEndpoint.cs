using System.Security.Claims;

namespace Ebi.Alas.Api.Features.Auth.ChangePassword;

public static class ChangePasswordEndpoint
{
    public static void MapChangePassword(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/change-password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal user,
            ChangePasswordHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            if (!Guid.TryParse(sub, out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "A valid user session is required.");
            }

            var outcome = await handler.HandleAsync(userId, request, cancellationToken);

            if (outcome is ChangePasswordOutcome.Success)
            {
                RefreshCookie.Delete(httpContext);
                return Results.NoContent();
            }

            if (outcome is ChangePasswordOutcome.Failure failure)
            {
                return Results.Problem(
                    statusCode: failure.StatusCode,
                    title: failure.StatusCode == StatusCodes.Status400BadRequest ? "Bad Request" : "Not Found",
                    detail: failure.Detail);
            }

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Server Error",
                detail: "An unexpected error occurred.");
        })
        .RequireAuthorization()
        .RequireRateLimiting("auth")
        .WithName("ChangePassword")
        .WithTags("Auth");
    }
}
