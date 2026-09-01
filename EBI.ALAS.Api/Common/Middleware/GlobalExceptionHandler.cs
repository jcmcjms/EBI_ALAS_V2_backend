using System.Net;
using System.Text.Json;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Models;
using FluentValidation;

namespace EBI.ALAS.Api.Common.Middleware;

public sealed class GlobalExceptionHandler
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandler> _logger;

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
            _ => (HttpStatusCode.InternalServerError, ApiResponse.ErrorResponse("An unexpected error occurred. Please try again later."))
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

    private static ApiResponse BuildValidationResponse(ValidationException validationException)
    {
        var errorsByField = validationException.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToList());

        var flatErrors = errorsByField.SelectMany(kvp => kvp.Value).ToList();
        return ApiResponse.ErrorResponse("Validation failed", flatErrors);
    }
}
