using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ebi.Alas.Api.Composition.Errors;

public static class ApiProblemHandler
{
    private const int ClientClosedRequestStatus = 499;

    public static ProblemDetails Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            NotFoundException ex => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = ex.Message
            },
            ForbiddenException ex => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = ex.Message
            },
            ConflictException ex => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = ex.Message
            },
            BadHttpRequestException ex => new ProblemDetails
            {
                Status = ex.StatusCode,
                Title = "Bad Request",
                Detail = "The request could not be processed."
            },
            OperationCanceledException => new ProblemDetails
            {
                Status = ClientClosedRequestStatus,
                Title = "Cancelled",
                Detail = "The request was cancelled."
            },
            Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service Unavailable",
                Detail = "A required data store is unavailable."
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server Error",
                Detail = "An unexpected error occurred."
            }
        };
    }

    public static IExceptionHandler CreateHandler(IProblemDetailsService problemDetailsService)
    {
        ArgumentNullException.ThrowIfNull(problemDetailsService);

        return new Handler(problemDetailsService);
    }

    private sealed class Handler(IProblemDetailsService problemDetailsService) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var problem = Map(exception);
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception
            });
        }
    }
}
