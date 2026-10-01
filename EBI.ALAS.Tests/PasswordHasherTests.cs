using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Tests;
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();
    [Fact]
    public void HashPassword_TemporaryWorkFactor_Produces_WF12_Hash()
    {
        var hash = _hasher.HashPassword("test_password", IPasswordHasher.TemporaryWorkFactor);
        Assert.StartsWith("$2a$12$", hash);
    }
    [Fact]
    public void HashPassword_InteractiveWorkFactor_Produces_WF14_Hash()
    {
        var hash = _hasher.HashPassword("test_password", IPasswordHasher.InteractiveWorkFactor);
        Assert.StartsWith("$2a$14$", hash);
    }
    [Fact]
    public void HashPassword_DefaultParameter_Produces_WF14_Hash()
    {
        var hash = _hasher.HashPassword("test_password");
        Assert.StartsWith("$2a$14$", hash);
    }
    [Fact]
    public void VerifyPassword_Succeeds_Against_WF12_Hash()
    {
        var password = "Str0ng!Pass";
        var hash = _hasher.HashPassword(password, IPasswordHasher.TemporaryWorkFactor);
        Assert.True(_hasher.VerifyPassword(password, hash));
    }
    [Fact]
    public void VerifyPassword_Succeeds_Against_WF14_Hash()
    {
        var password = "Str0ng!Pass";
        var hash = _hasher.HashPassword(password, IPasswordHasher.InteractiveWorkFactor);
        Assert.True(_hasher.VerifyPassword(password, hash));
    }
    [Fact]
    public void VerifyPassword_Rejects_Wrong_Password()
    {
        var hash = _hasher.HashPassword("correct_password", IPasswordHasher.TemporaryWorkFactor);
        Assert.False(_hasher.VerifyPassword("wrong_password", hash));
    }
    [Fact]
    public void VerifyPassword_Handles_Invalid_Hash_Format_Gracefully()
    {
        Assert.False(_hasher.VerifyPassword("any_password", "not_a_valid_bcrypt_hash"));
    }
    [Theory]
    [InlineData(IPasswordHasher.TemporaryWorkFactor)]
    [InlineData(IPasswordHasher.InteractiveWorkFactor)]
    public void VerifyPassword_Mixed_Cost_Hashes_Verify_Correctly(int workFactor)
    {
        var password = "Banking!Grade2024";
        var hash = _hasher.HashPassword(password, workFactor);
        Assert.True(_hasher.VerifyPassword(password, hash));
    }
    [Fact]
    public void TemporaryWorkFactor_Is_Less_Than_InteractiveWorkFactor()
    {
        Assert.True(IPasswordHasher.TemporaryWorkFactor < IPasswordHasher.InteractiveWorkFactor);
    }
}
