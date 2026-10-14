using System.ComponentModel.DataAnnotations;
using Ebi.Alas.Api.Infrastructure.Email;

namespace Ebi.Alas.Api.Tests.Infrastructure.Email;

public sealed class EmailOptionsTests
{
    [Fact]
    public void Validate_MissingHost_Fails()
    {
        var options = new EmailOptions
        {
            Host = "",
            Port = 587,
            From = "alas@example.com",
            UserName = "u",
            Password = "p",
            EnableSsl = true
        };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmailOptions.Host)));
    }

    [Fact]
    public void Validate_CompleteOptions_Succeeds()
    {
        var options = new EmailOptions
        {
            Host = "smtp.example.com",
            Port = 587,
            From = "alas@example.com",
            UserName = "u",
            Password = "p",
            EnableSsl = true
        };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.True(valid);
    }
}
