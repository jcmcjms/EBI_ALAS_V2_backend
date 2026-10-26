using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class ESignaturePolicyTests
{
    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", true)]
    [InlineData("data:image/jpeg;base64,/9j/4AAQ=", false)]
    [InlineData("data:image/webp;base64,UklGRg==", false)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=", false)]
    [InlineData("data:text/html;base64,PGgxPg==", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsValidUpload_is_png_only(string? input, bool expected)
        => Assert.Equal(expected, ESignaturePolicy.IsValidUpload(input));

    [Fact]
    public void IsValidUpload_rejects_oversized_ink()
    {
        var oversized = "data:image/png;base64," + new string('A', ESignaturePolicy.MaxLength);
        Assert.False(ESignaturePolicy.IsValidUpload(oversized));
    }

    [Fact]
    public void SanitizeForDisplay_keeps_legacy_readable_ink()
    {
        Assert.Null(ESignaturePolicy.SanitizeForDisplay("data:image/svg+xml;base64,PHN2Zz4="));
        Assert.Equal(
            "data:image/png;base64,iVBORw0KGgo=",
            ESignaturePolicy.SanitizeForDisplay("data:image/png;base64,iVBORw0KGgo="));
        Assert.Equal(
            "data:image/jpeg;base64,/9j/4AAQ=",
            ESignaturePolicy.SanitizeForDisplay("data:image/jpeg;base64,/9j/4AAQ="));
    }

    [Fact]
    public void IsEligibleForSigning_requires_png_display_ink()
    {
        Assert.True(ESignaturePolicy.IsEligibleForSigning("data:image/png;base64,iVBORw0KGgo="));
        Assert.False(ESignaturePolicy.IsEligibleForSigning("data:image/jpeg;base64,/9j/4AAQ="));
        Assert.False(ESignaturePolicy.IsEligibleForSigning("data:image/webp;base64,UklGRg=="));
        Assert.False(ESignaturePolicy.IsEligibleForSigning(null));
    }

    [Fact]
    public void Sanitize_alias_matches_display_path()
    {
        const string ink = "data:image/png;base64,iVBORw0KGgo=";
        Assert.Equal(ESignaturePolicy.SanitizeForDisplay(ink), ESignaturePolicy.Sanitize(ink));
    }
}
