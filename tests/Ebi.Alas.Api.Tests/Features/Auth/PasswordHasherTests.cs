using Ebi.Alas.Api.Features.Auth;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("Correct-Horse-1!");

        Assert.True(hasher.Verify("Correct-Horse-1!", hash));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("Correct-Horse-1!");

        Assert.False(hasher.Verify("wrong", hash));
    }
}
