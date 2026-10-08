using Ebi.Alas.Api.Features.Auth.Refresh;

namespace Ebi.Alas.Api.Features.Auth.Login;

public static class LoginEndpoint
{
    public static void MapLogin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", async (
            LoginRequest request,
            LoginHandler handler,
            HttpContext httpContext,
            Microsoft.Extensions.Options.IOptions<Composition.ApiOptions> options,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request, cancellationToken);

            if (outcome is LoginOutcome.Success success)
            {
                RefreshCookie.Append(
                    httpContext,
                    success.Response.RefreshToken,
                    timeProvider.GetUtcNow().AddDays(options.Value.Jwt.RefreshTokenAbsoluteDays));
                return Results.Ok(success.Response);
            }

            if (outcome is LoginOutcome.Failure failure)
            {
                return Results.Problem(
                    statusCode: failure.StatusCode,
                    title: failure.StatusCode == StatusCodes.Status401Unauthorized ? "Unauthorized" : "Forbidden",
                    detail: failure.Detail);
            }

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Server Error",
                detail: "An unexpected error occurred.");
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithName("Login")
        .WithTags("Auth");
    }
}
