using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class ESignaturePolicyTests
{
    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", true)]
    [InlineData("data:image/jpeg;base64,/9j/4AAQ=", true)]
    [InlineData("data:image/webp;base64,UklGRg==", true)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=", false)]
    [InlineData("data:text/html;base64,PGgxPg==", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsValid_enforces_mime_and_format(string? input, bool expected)
        => Assert.Equal(expected, ESignaturePolicy.IsValid(input));

    [Fact]
    public void IsValid_rejects_oversized_ink()
    {
        var oversized = "data:image/png;base64," + new string('A', ESignaturePolicy.MaxLength);
        Assert.False(ESignaturePolicy.IsValid(oversized));
    }

    [Fact]
    public void Sanitize_returnsNullForUnsafeInk()
    {
        Assert.Null(ESignaturePolicy.Sanitize("data:image/svg+xml;base64,PHN2Zz4="));
        Assert.Equal(
            "data:image/png;base64,iVBORw0KGgo=",
            ESignaturePolicy.Sanitize("data:image/png;base64,iVBORw0KGgo="));
    }
}
