using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.WebLoans;

public sealed class WebLoanCisLookupTests
{
    [Fact]
    public void WebLoanAccountId_FormatsAndParses()
    {
        Assert.Equal("011-05-1", WebLoanAccountId.Format("011", "05-1"));
        var (branch, account) = WebLoanAccountId.Parse("011-05-13081-1");
        Assert.Equal("011", branch);
        Assert.Equal("05-13081-1", account);
    }

    [Fact]
    public void PendingLoanDto_AcceptsByteCreationType()
    {
        byte creationType = 1;
        var dto = new PendingLoanDto(
            "LN-1",
            1000m,
            0.05m,
            30,
            6,
            "C16 - AFOS",
            null,
            creationType,
            "Reloan",
            10m);
        Assert.Equal((byte?)1, dto.CreationType);
    }

    [Fact]
    public void OutstandingLoanDto_IncludesStatusAndAmort()
    {
        var dto = new OutstandingLoanDto(
            "LN-1", 1000m, 800m, 250m,
            "2024-01-01", "2025-01-01",
            "A13", "A13 - Current", "A13 - AFOS");
        Assert.Equal("A13 - Current", dto.ProductStatus);
        Assert.Equal(250m, dto.AmortAmount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void ComputeLengthOfService_InvalidHireDate_ReturnsNull(string? raw)
    {
        Assert.Null(WebLoanReader.ComputeLengthOfService(raw));
    }

    [Fact]
    public void ComputeLengthOfService_ValidHireDate_ReturnsYearMonthSummary()
    {
        var result = WebLoanReader.ComputeLengthOfService("2010-01-01");
        Assert.NotNull(result);
        Assert.Contains("years", result);
        Assert.Contains("months", result);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2010-01-01", "2010-01-01")]
    [InlineData("01/15/2010", "2010-01-15")]
    [InlineData("2010/01/15", "2010-01-15")]
    public void ParseBirthDate_NormalizesKnownFormats(string? raw, string? expected)
    {
        Assert.Equal(expected, WebLoanReader.ParseBirthDate(raw));
    }
}
