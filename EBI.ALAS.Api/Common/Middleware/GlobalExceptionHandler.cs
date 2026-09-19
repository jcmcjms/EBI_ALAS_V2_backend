using System.Net;
using System.Text.Json;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Global exception handler that maps domain exceptions to RFC 7807 ProblemDetails-style responses.
/// Uses primary constructor for dependency injection.
/// </summary>
public sealed class GlobalExceptionHandler(
    RequestDelegate next,
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, response) = exception switch
        {
            ValidationException validationEx => (HttpStatusCode.BadRequest, BuildValidationResponse(validationEx)),
            NotFoundException notFoundEx => (HttpStatusCode.NotFound, ApiResponse.ErrorResponse(notFoundEx.Message)),
            ForbiddenAccessException forbiddenEx => (HttpStatusCode.Forbidden, ApiResponse.ErrorResponse(forbiddenEx.Message)),
            InvalidWorkflowException workflowEx => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse(workflowEx.Message)),
            UnauthorizedAccessException unauthorizedEx => (HttpStatusCode.Unauthorized, ApiResponse.ErrorResponse(unauthorizedEx.Message)),
            ArgumentException argEx => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse(argEx.Message)),
            InvalidOperationException opEx => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse(opEx.Message)),
            CapacityGateException capacityEx => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse(capacityEx.Message, capacityEx.Errors.SelectMany(e => e.Value).ToList())),
            DbUpdateConcurrencyException => (HttpStatusCode.Conflict, ApiResponse.ErrorResponse("The record was modified by another user. Please refresh and try again.")),
            DbUpdateException dbEx => HandleDbUpdateException(dbEx),
            OperationCanceledException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse("Request was cancelled.")),
            _ => HandleUnhandledException(exception)
        };

        if (statusCode >= HttpStatusCode.InternalServerError)
            logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        else
            logger.LogWarning(exception, "Handled exception ({StatusCode}): {Message}", (int)statusCode, exception.Message);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.Serialize(response, JsonOptions);
        await context.Response.WriteAsync(payload);
    }

    private (HttpStatusCode statusCode, ApiResponse response) HandleDbUpdateException(DbUpdateException dbEx)
    {
        var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;

        if (innerMessage.Contains("2627") || innerMessage.Contains("2601") || innerMessage.Contains("UNIQUE"))
        {
            logger.LogWarning(dbEx, "Unique constraint violation: {Message}", innerMessage);
            return (HttpStatusCode.Conflict, ApiResponse.ErrorResponse("A record with this value already exists."));
        }

        if (innerMessage.Contains("547") || innerMessage.Contains("FOREIGN KEY"))
        {
            logger.LogWarning(dbEx, "Foreign key constraint violation: {Message}", innerMessage);
            return (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse("Referenced record does not exist."));
        }

        return environment.IsDevelopment()
            ? (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse($"Database error: {innerMessage}"))
            : (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse("A database error occurred. Please try again later."));
    }

    private (HttpStatusCode statusCode, ApiResponse response) HandleUnhandledException(Exception exception)
    {
        if (environment.IsDevelopment())
        {
            var errorMessage = exception.Message;
            if (exception.InnerException is not null)
                errorMessage += $" | Inner: {exception.InnerException.Message}";

            return (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse(errorMessage));
        }

        return (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse("An unexpected error occurred. Please try again later."));
    }

    private static ApiResponse BuildValidationResponse(ValidationException validationException)
    {
        var flatErrors = validationException.Errors
            .GroupBy(e => e.PropertyName)
            .SelectMany(g => g.Select(e => e.ErrorMessage))
            .ToList();

        return ApiResponse.ErrorResponse("Validation failed", flatErrors);
    }
}
