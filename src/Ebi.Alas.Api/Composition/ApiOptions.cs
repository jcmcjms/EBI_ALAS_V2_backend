using System.ComponentModel.DataAnnotations;

namespace Ebi.Alas.Api.Composition;

public sealed class ApiOptions : IValidatableObject
{
    public const string SectionName = "Api";

    public const int MaximumPageSize = 1000;

    public JwtOptions Jwt { get; set; } = new();

    public int DefaultPageSize { get; set; } = 20;

    public int MaxPageSize { get; set; } = 100;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        return ValidateCore();
    }

    private List<ValidationResult> ValidateCore()
    {
        List<ValidationResult> results = [.. Jwt.Validate(nameof(Jwt))];

        if (DefaultPageSize < 1)
        {
            results.Add(new ValidationResult(
                $"{nameof(DefaultPageSize)} must be at least 1.",
                [nameof(DefaultPageSize)]));
        }

        if (MaxPageSize < 1)
        {
            results.Add(new ValidationResult(
                $"{nameof(MaxPageSize)} must be at least 1.",
                [nameof(MaxPageSize)]));
        }
        else if (MaxPageSize > MaximumPageSize)
        {
            results.Add(new ValidationResult(
                $"{nameof(MaxPageSize)} must be at most {MaximumPageSize}.",
                [nameof(MaxPageSize)]));
        }

        if (DefaultPageSize > MaxPageSize)
        {
            results.Add(new ValidationResult(
                $"{nameof(DefaultPageSize)} must not exceed {nameof(MaxPageSize)}.",
                [nameof(DefaultPageSize), nameof(MaxPageSize)]));
        }

        return results;
    }
}

public sealed class JwtOptions
{
    public const int MinimumSigningKeyLength = 32;

    public const int MaximumAccessTokenMinutes = 120;

    public const int MaximumRefreshTokenDays = 30;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;

    public int RefreshTokenAbsoluteDays { get; set; } = 14;

    public IEnumerable<ValidationResult> Validate(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        List<ValidationResult> results = [];

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(Issuer)} is required.",
                [$"{prefix}:{nameof(Issuer)}"]));
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(Audience)} is required.",
                [$"{prefix}:{nameof(Audience)}"]));
        }

        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(SigningKey)} is required.",
                [$"{prefix}:{nameof(SigningKey)}"]));
        }
        else if (SigningKey.Length < MinimumSigningKeyLength)
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(SigningKey)} must be at least {MinimumSigningKeyLength} characters.",
                [$"{prefix}:{nameof(SigningKey)}"]));
        }

        if (AccessTokenMinutes < 1)
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(AccessTokenMinutes)} must be at least 1.",
                [$"{prefix}:{nameof(AccessTokenMinutes)}"]));
        }
        else if (AccessTokenMinutes > MaximumAccessTokenMinutes)
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(AccessTokenMinutes)} must be at most {MaximumAccessTokenMinutes}.",
                [$"{prefix}:{nameof(AccessTokenMinutes)}"]));
        }

        if (RefreshTokenDays < 1)
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(RefreshTokenDays)} must be at least 1.",
                [$"{prefix}:{nameof(RefreshTokenDays)}"]));
        }
        else if (RefreshTokenDays > MaximumRefreshTokenDays)
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(RefreshTokenDays)} must be at most {MaximumRefreshTokenDays}.",
                [$"{prefix}:{nameof(RefreshTokenDays)}"]));
        }

        if (RefreshTokenAbsoluteDays < RefreshTokenDays)
        {
            results.Add(new ValidationResult(
                $"{prefix}:{nameof(RefreshTokenAbsoluteDays)} must be at least {nameof(RefreshTokenDays)}.",
                [$"{prefix}:{nameof(RefreshTokenAbsoluteDays)}"]));
        }

        return results;
    }
}
