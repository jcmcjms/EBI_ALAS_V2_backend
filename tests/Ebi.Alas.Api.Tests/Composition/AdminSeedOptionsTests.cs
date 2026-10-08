using System.ComponentModel.DataAnnotations;
using Ebi.Alas.Api.Composition;

namespace Ebi.Alas.Api.Tests.Composition;

public sealed class AdminSeedOptionsTests
{
    private static List<ValidationResult> Validate(AdminSeedOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Validate_MissingFields_ReturnsErrors()
    {
        var results = Validate(new AdminSeedOptions());
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminSeedOptions.AdminUserName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminSeedOptions.AdminPassword)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminSeedOptions.AdminBranchId)));
    }

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var results = Validate(new AdminSeedOptions
        {
            AdminUserName = "admin",
            AdminPassword = "at-least-12-chars-Aa1!",
            AdminBranchId = "BR-HQ",
            AdminFirstName = "System",
            AdminLastName = "Administrator",
            AdminEmail = "admin@example.invalid"
        });
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_ShortPassword_ReturnsError()
    {
        var results = Validate(new AdminSeedOptions
        {
            AdminUserName = "admin",
            AdminPassword = "short",
            AdminBranchId = "BR-HQ"
        });
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminSeedOptions.AdminPassword)));
    }
}
