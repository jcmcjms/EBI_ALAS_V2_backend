using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ebi.Alas.Api.Composition.Errors;

/// <summary>
/// Converts expected request failures into ProblemDetails inside user middleware
/// so they never escape to the framework exception pipeline (which debuggers
/// report as user-unhandled). Covers domain errors and request cancellation
/// (client disconnect / aborted requests after session revoke).
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
            catch (OperationCanceledException)
            {
                // Client abort (navigation, tab close, force-logout after session
                // revoke) cancels RequestAborted and surfaces TaskCanceledException
                // from EF/SignalR. Swallow here so the debugger never reports it
                // as user-unhandled and the process is not interrupted.
                if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested)
                {
                    return;
                }

                context.Response.StatusCode = 499;
                try
                {
                    await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = 499,
                            Title = "Cancelled",
                            Detail = "The request was cancelled."
                        },
                    });
                }
                catch (OperationCanceledException)
                {
                    // Client vanished while writing the problem body.
                }
            }
        });
    }
}
