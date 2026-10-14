namespace Ebi.Alas.Api.Features.Auth.ForgotPassword;

public static class ForgotPasswordEndpoint
{
    public static void MapForgotPassword(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/forgot-password", async (
            ForgotPasswordRequest request,
            ForgotPasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request, cancellationToken);

            if (outcome is ForgotPasswordOutcome.Success success)
            {
                return Results.Ok(new ForgotPasswordResponse(success.Message));
            }

            if (outcome is ForgotPasswordOutcome.Failure failure)
            {
                var title = failure.StatusCode switch
                {
                    StatusCodes.Status400BadRequest => "Bad Request",
                    StatusCodes.Status404NotFound => "Not Found",
                    StatusCodes.Status429TooManyRequests => "Too Many Requests",
                    _ => "Server Error"
                };
                return Results.Problem(
                    statusCode: failure.StatusCode,
                    title: title,
                    detail: failure.Detail);
            }

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Server Error",
                detail: "An unexpected error occurred.");
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithName("ForgotPassword")
        .WithTags("Auth");
    }
}
