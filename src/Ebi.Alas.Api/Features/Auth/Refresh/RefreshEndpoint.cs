using Ebi.Alas.Api.Features.Auth.Refresh;

namespace Ebi.Alas.Api.Features.Auth.Refresh;

public static class RefreshEndpoint
{
    public static void MapRefresh(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/refresh", async (
            HttpContext httpContext,
            RefreshHandler handler,
            Microsoft.Extensions.Options.IOptions<Composition.ApiOptions> options,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            RefreshRequest? request = null;
            if (httpContext.Request.ContentLength is > 0)
            {
                request = await httpContext.Request.ReadFromJsonAsync<RefreshRequest>(cancellationToken);
            }

            var raw = !string.IsNullOrWhiteSpace(request?.RefreshToken)
                ? request.RefreshToken
                : RefreshCookie.Read(httpContext);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Refresh token is required.");
            }

            var outcome = await handler.HandleAsync(
                new RefreshRequest { RefreshToken = raw },
                cancellationToken);

            if (outcome is RefreshOutcome.Success success)
            {
                var absoluteDays = options.Value.Jwt.RefreshTokenAbsoluteDays;
                RefreshCookie.Append(
                    httpContext,
                    success.Response.RefreshToken,
                    timeProvider.GetUtcNow().AddDays(absoluteDays));
                return Results.Ok(success.Response);
            }

            if (outcome is RefreshOutcome.Failure failure)
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
        .WithName("RefreshToken")
        .WithTags("Auth");
    }
}
