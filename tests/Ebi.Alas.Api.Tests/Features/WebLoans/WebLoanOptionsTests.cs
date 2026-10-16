using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.WebLoans;

public sealed class WebLoanOptionsTests
{
    [Theory]
    [InlineData("webloan.dbo", "cis_info", "webloan.dbo.cis_info")]
    [InlineData("WEBLOAN_LINKED.webloan.dbo", "loan_data", "WEBLOAN_LINKED.webloan.dbo.loan_data")]
    [InlineData("", "cis_info", "cis_info")]
    [InlineData(null, "cis_info", "cis_info")]
    public void Qualify_JoinsPrefixAndTable(string? prefix, string table, string expected)
    {
        Assert.Equal(expected, WebLoanSql.Qualify(prefix!, table));
    }

    [Theory]
    [InlineData("webloan.dbo")]
    [InlineData("WEBLOAN_LINKED.webloan.dbo")]
    [InlineData("webloan")]
    public void NamePrefix_Valid_identifiers_pass(string prefix)
    {
        Assert.True(WebLoanOptions.IsValidNamePrefix(prefix));
    }

    [Theory]
    [InlineData("")]
    [InlineData("webloan.dbo; DROP")]
    [InlineData("webloan..dbo")]
    [InlineData("webloan dbo")]
    [InlineData("webloan.dbo-")]
    public void NamePrefix_Invalid_rejected(string prefix)
    {
        Assert.False(WebLoanOptions.IsValidNamePrefix(prefix));
    }
}
