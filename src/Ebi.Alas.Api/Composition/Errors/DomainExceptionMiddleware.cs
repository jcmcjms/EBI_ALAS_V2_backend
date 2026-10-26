using Microsoft.AspNetCore.Http;

namespace Ebi.Alas.Api.Composition.Errors;

/// <summary>
/// Converts domain exceptions into ProblemDetails inside user middleware so
/// expected validation never escapes to the framework exception pipeline
/// (which debuggers report as user-unhandled).
/// </summary>
public static class DomainExceptionMiddleware
{
    public static IApplicationBuilder UseDomainExceptions(
        this IApplicationBuilder app,
        IProblemDetailsService problemDetailsService)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(problemDetailsService);

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex) when (ex is ValidationException
                or NotFoundException
                or ForbiddenException
                or ConflictException)
            {
                var problem = ApiProblemHandler.Map(ex);
                context.Response.StatusCode =
                    problem.Status ?? StatusCodes.Status500InternalServerError;

                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = problem,
                    Exception = ex,
                });
            }
        });
    }
}
