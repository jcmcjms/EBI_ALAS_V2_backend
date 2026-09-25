using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Tests;

/// <summary>
/// Tests for the two-tier BCrypt cost strategy in PasswordHasher.
/// Verifies that temporary credentials use WF12 (fast) while interactive
/// credentials use WF14 (banking-grade), and that BCrypt's self-describing
/// hash format allows mixed-cost verification.
/// </summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_TemporaryWorkFactor_Produces_WF12_Hash()
    {
        var hash = _hasher.HashPassword("test_password", IPasswordHasher.TemporaryWorkFactor);

        // BCrypt hash format: $2a$<workfactor>$...
        // WF12 → "$2a$12$"
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
        // Callers using the single-arg overload get WF14 (interactive default)
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

        // BCrypt hashes are self-describing — the cost is embedded in the hash.
        // A WF12 hash verifies correctly against WF14 verify call and vice versa.
        Assert.True(_hasher.VerifyPassword(password, hash));
    }

    [Fact]
    public void TemporaryWorkFactor_Is_Less_Than_InteractiveWorkFactor()
    {
        Assert.True(IPasswordHasher.TemporaryWorkFactor < IPasswordHasher.InteractiveWorkFactor);
    }
}