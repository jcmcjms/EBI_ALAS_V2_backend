using Ebi.Alas.Api.Features.Auth.ForgotPassword;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class TemporaryPasswordGeneratorTests
{
    [Fact]
    public void Generate_MeetsChangePasswordPolicy()
    {
        var password = TemporaryPasswordGenerator.Generate();

        Assert.True(password.Length >= 12);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => "!?*.".Contains(c));
    }

    [Fact]
    public void Generate_TwoCalls_Differ()
    {
        Assert.NotEqual(TemporaryPasswordGenerator.Generate(), TemporaryPasswordGenerator.Generate());
    }
}
