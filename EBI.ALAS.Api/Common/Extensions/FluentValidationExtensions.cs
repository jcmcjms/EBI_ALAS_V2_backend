using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using FluentValidation.Results;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Extension methods that convert FluentValidation results into the
/// consistent <see cref="ApiResponse"/> error format consumed by the frontend.
/// </summary>
public static class FluentValidationExtensions
{
    /// <summary>
    /// Converts a <see cref="ValidationResult"/> into an <see cref="ApiResponse"/>
    /// with HTTP 400 semantics. The <c>Errors</c> dictionary is keyed by field name
    /// and each value is the list of failure messages for that field.
    /// </summary>
    /// <param name="validationResult">The FluentValidation result.</param>
    /// <param name="message">Optional top-level message (defaults to "Validation failed").</param>
    /// <returns>An <see cref="ApiResponse"/> suitable for <c>Results.BadRequest()</c>.</returns>
    public static ApiResponse ToApiResponse(this ValidationResult validationResult, string? message = null)
    {
        var errors = validationResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToList());

        var flatErrors = errors
            .SelectMany(kvp => kvp.Value)
            .ToList();

        return ApiResponse.ErrorResponse(
            message ?? "Validation failed",
            flatErrors);
    }

    /// <summary>
    /// Converts a <see cref="ValidationResult"/> into a structured
    /// <c>Dictionary&lt;string, List&lt;string&gt;&gt;</c> for field-level error mapping.
    /// Useful when the caller wants to build a custom error shape.
    /// </summary>
    /// <param name="validationResult">The FluentValidation result.</param>
    /// <returns>A dictionary mapping field names to their error messages.</returns>
    public static Dictionary<string, List<string>> ToErrorDictionary(this ValidationResult validationResult)
    {
        return validationResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToList());
    }

    /// <summary>
    /// Throws <see cref="ValidationException"/> when the result is invalid.
    /// Useful in service/repository layers where the GlobalExceptionHandler
    /// will catch it and return a 400 response automatically.
    /// </summary>
    /// <param name="validationResult">The FluentValidation result.</param>
    /// <exception cref="ValidationException">Thrown when validation fails.</exception>
    public static void ThrowIfInvalid(this ValidationResult validationResult)
    {
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }
    }
}
