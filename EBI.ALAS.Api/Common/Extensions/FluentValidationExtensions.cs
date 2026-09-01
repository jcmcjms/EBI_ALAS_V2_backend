using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using FluentValidation.Results;

namespace EBI.ALAS.Api.Common.Extensions;
public static class FluentValidationExtensions
{
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
    public static Dictionary<string, List<string>> ToErrorDictionary(this ValidationResult validationResult)
    {
        return validationResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToList());
    }
    public static void ThrowIfInvalid(this ValidationResult validationResult)
    {
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }
    }
}
