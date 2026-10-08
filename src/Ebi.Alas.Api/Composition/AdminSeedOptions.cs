using System.ComponentModel.DataAnnotations;

namespace Ebi.Alas.Api.Composition;

public sealed class AdminSeedOptions : IValidatableObject
{
    public const string SectionName = "Seed";

    public const int MinimumPasswordLength = 12;

    public string AdminUserName { get; set; } = string.Empty;

    public string AdminPassword { get; set; } = string.Empty;

    public string AdminFirstName { get; set; } = string.Empty;

    public string AdminLastName { get; set; } = string.Empty;

    public string AdminEmail { get; set; } = string.Empty;

    public string AdminBranchId { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (string.IsNullOrWhiteSpace(AdminUserName))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(AdminUserName)} is required.",
                [nameof(AdminUserName)]);
        }

        if (string.IsNullOrWhiteSpace(AdminPassword))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(AdminPassword)} is required. Set with: dotnet user-secrets set \"{SectionName}:{nameof(AdminPassword)}\" \"...\" --project src/Ebi.Alas.Api",
                [nameof(AdminPassword)]);
        }
        else if (AdminPassword.Length < MinimumPasswordLength)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(AdminPassword)} must be at least {MinimumPasswordLength} characters.",
                [nameof(AdminPassword)]);
        }

        if (string.IsNullOrWhiteSpace(AdminBranchId))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(AdminBranchId)} is required.",
                [nameof(AdminBranchId)]);
        }
    }
}
