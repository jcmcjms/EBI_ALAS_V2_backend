using System.Net;
using System.Text.Json;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Common.Middleware;

public sealed class GlobalExceptionHandler
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context); }
        catch (Exception ex) { await HandleExceptionAsync(context, ex); }
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

            // EF Core concurrency conflicts — two users editing the same record
            DbUpdateConcurrencyException concurrencyEx =>
                (HttpStatusCode.Conflict, ApiResponse.ErrorResponse("The record was modified by another user. Please refresh and try again.")),

            // EF Core database errors — FK violations, unique constraint violations, etc.
            DbUpdateException dbEx => HandleDbUpdateException(dbEx),

            // OperationCanceledException — client disconnected or request timed out
            OperationCanceledException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse("Request was cancelled.")),

            _ => HandleUnhandledException(exception)
        };

        if (statusCode >= HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        else
            _logger.LogWarning(exception, "Handled exception ({StatusCode}): {Message}", (int)statusCode, exception.Message);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.Serialize(response, JsonOptions);
        await context.Response.WriteAsync(payload);
    }

    private (HttpStatusCode statusCode, ApiResponse response) HandleDbUpdateException(DbUpdateException dbEx)
    {
        // Check for common SQL Server error codes
        var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;

        // Unique constraint violation (SQL Server error 2627/2601)
        if (innerMessage.Contains("2627") || innerMessage.Contains("2601") || innerMessage.Contains("UNIQUE"))
        {
            _logger.LogWarning(dbEx, "Unique constraint violation: {Message}", innerMessage);
            return (HttpStatusCode.Conflict, ApiResponse.ErrorResponse("A record with this value already exists."));
        }

        // Foreign key constraint violation (SQL Server error 547)
        if (innerMessage.Contains("547") || innerMessage.Contains("FOREIGN KEY"))
        {
            _logger.LogWarning(dbEx, "Foreign key constraint violation: {Message}", innerMessage);
            return (HttpStatusCode.BadRequest, ApiResponse.ErrorResponse("Referenced record does not exist."));
        }

        // In development, include details for debugging
        if (_environment.IsDevelopment())
        {
            return (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse($"Database error: {innerMessage}"));
        }

        return (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse("A database error occurred. Please try again later."));
    }

    private (HttpStatusCode statusCode, ApiResponse response) HandleUnhandledException(Exception exception)
    {
        // In development, include the actual error message for debugging
        if (_environment.IsDevelopment())
        {
            var errorMessage = $"{exception.Message}";
            if (exception.InnerException != null)
                errorMessage += $" | Inner: {exception.InnerException.Message}";

            return (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse(errorMessage));
        }

        // In production, return generic message
        return (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse("An unexpected error occurred. Please try again later."));
    }

    private static ApiResponse BuildValidationResponse(ValidationException validationException)
    {
        var errorsByField = validationException.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToList());

        var flatErrors = errorsByField.SelectMany(kvp => kvp.Value).ToList();
        return ApiResponse.ErrorResponse("Validation failed", flatErrors);
    }
}
