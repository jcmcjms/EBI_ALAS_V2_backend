using System.Net;
using System.Text.Json;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using FluentValidation.Results;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Middleware that catches all unhandled exceptions and returns a consistent
/// <see cref="ApiResponse"/> JSON envelope.  This sits early in the pipeline so
/// every downstream middleware and endpoint benefits from centralized error handling.
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    // Reusable JsonSerializerOptions cached for the hot path.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // ─── Determine status code & response ─────────────────────────────
        var (statusCode, response) = exception switch
        {
            // FluentValidation failures → 400
            ValidationException validationEx
                => (HttpStatusCode.BadRequest, BuildValidationResponse(validationEx)),

            // Custom NotFoundException → 404
            NotFoundException notFoundEx
                => (HttpStatusCode.NotFound, ApiResponse.ErrorResponse(notFoundEx.Message)),

            // Custom ForbiddenAccessException → 403
            ForbiddenAccessException forbiddenEx
                => (HttpStatusCode.Forbidden, ApiResponse.ErrorResponse(forbiddenEx.Message)),

            // Custom InvalidWorkflowException → 400
            InvalidWorkflowException workflowEx
                => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse(workflowEx.Message)),

            // Standard .NET unauthorized → 401
            UnauthorizedAccessException unauthorizedEx
                => (HttpStatusCode.Unauthorized, ApiResponse.ErrorResponse(unauthorizedEx.Message)),

            // Argument/operation exceptions → 400
            ArgumentException argEx
                => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse(argEx.Message)),

            InvalidOperationException opEx
                => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse(opEx.Message)),

            // Catch-all → 500
            _ => (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse(
                     "An unexpected error occurred. Please try again later."))
        };

        // ─── Log ───────────────────────────────────────────────────────────
        if (statusCode >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception ({StatusCode}): {Message}",
                (int)statusCode, exception.Message);
        }

        // ─── Write response ────────────────────────────────────────────────
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = JsonSerializer.Serialize(response, JsonOptions);
        await context.Response.WriteAsync(payload);
    }

    /// <summary>
    /// Builds a structured error response for FluentValidation exceptions,
    /// including per-field error arrays so the frontend can highlight individual inputs.
    /// </summary>
    private static ApiResponse BuildValidationResponse(ValidationException validationException)
    {
        // Group errors by property → list of messages
        var errorsByField = validationException.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToList());

        // Flatten for the top-level Errors list (backward-compatible)
        var flatErrors = errorsByField
            .SelectMany(kvp => kvp.Value)
            .ToList();

        return ApiResponse.ErrorResponse("Validation failed", flatErrors);
    }
}
