using EBI.ALAS.Api.Features.Users;

namespace EBI.ALAS.Tests;

/// <summary>
/// Tests for TempPasswordGenerator — the server-side password policy authority.
/// Verifies policy compliance over a statistical sample and edge cases.
/// </summary>
public class TempPasswordGeneratorTests
{
    private readonly TempPasswordGenerator _generator = new();

    // ── Policy compliance over 1 000 samples ────────────────────────────

    [Fact]
    public void Generate_SatisfiesCharacterClassPolicy_Over1000Samples()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!?*.";

        for (var i = 0; i < 1000; i++)
        {
            var password = _generator.Generate();

            Assert.Contains(password, c => upper.Contains(c));
            Assert.Contains(password, c => lower.Contains(c));
            Assert.Contains(password, c => digits.Contains(c));
            Assert.Contains(password, c => special.Contains(c));
        }
    }

    [Fact]
    public void Generate_ContainsNoAmbiguousCharacters_Over1000Samples()
    {
        // The generator excludes: O (uppercase o), 0 (zero), I (uppercase i),
        // l (lowercase L), 1 (one). Lowercase 'i' IS allowed — it's not ambiguous.
        const string ambiguous = "O0Il1";

        for (var i = 0; i < 1000; i++)
        {
            var password = _generator.Generate();
            Assert.DoesNotContain(password, c => ambiguous.Contains(c));
        }
    }

    [Fact]
    public void Generate_DefaultLength_IsTwelve()
    {
        var password = _generator.Generate();
        Assert.Equal(12, password.Length);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Generate_RespectsLengthParameter(int length)
    {
        var password = _generator.Generate(length);
        Assert.Equal(length, password.Length);
    }

    [Fact]
    public void Generate_ClampsLengthToMinimum()
    {
        var password = _generator.Generate(1);
        Assert.Equal(8, password.Length);
    }

    [Fact]
    public void Generate_ClampsLengthToMaximum()
    {
        var password = _generator.Generate(100);
        Assert.Equal(64, password.Length);
    }

    // ── Deterministic properties ─────────────────────────────────────────

    [Fact]
    public void Generate_ProducesDifferentPasswordsOnSuccessiveCalls()
    {
        var passwords = new HashSet<string>();
        for (var i = 0; i < 100; i++)
            passwords.Add(_generator.Generate());

        // With 12-char passwords from a 54-char pool, collision in 100
        // draws is astronomically unlikely. Assert we got at least 99
        // unique values (allowing for one cosmic-ray collision).
        Assert.True(passwords.Count >= 99);
    }

    [Fact]
    public void Generate_OnlyContainsAllowedCharacters()
    {
        const string allowed = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!?*.";

        for (var i = 0; i < 100; i++)
        {
            var password = _generator.Generate();
            Assert.All(password.ToCharArray(), c => Assert.Contains(c, allowed));
        }
    }
}
