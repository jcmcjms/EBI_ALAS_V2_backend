using System.ComponentModel.DataAnnotations;
using Ebi.Alas.Api.Infrastructure.Email;

namespace Ebi.Alas.Api.Tests.Infrastructure.Email;

public sealed class EmailOptionsTests
{
    [Fact]
    public void Validate_MissingFrom_Fails()
    {
        var options = new EmailOptions
        {
            Host = "",
            Port = 587,
            From = "",
            UserName = "u",
            Password = "p",
            EnableSsl = true
        };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmailOptions.From)));
    }

    [Fact]
    public void Validate_ResendStyleOptions_Succeeds()
    {
        var options = new EmailOptions
        {
            Provider = "Resend",
            ApiKey = "re_test",
            From = "ALAS <alas@example.com>"
        };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.True(valid);
    }
}
