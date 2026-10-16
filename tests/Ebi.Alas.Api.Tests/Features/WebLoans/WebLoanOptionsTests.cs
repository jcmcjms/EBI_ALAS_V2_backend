using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.WebLoans;

public sealed class WebLoanOptionsTests
{
    [Theory]
    [InlineData("webloan")]
    [InlineData("WebLoan")]
    [InlineData("web_loan")]
    public void Database_Valid_identifiers_pass(string database)
    {
        Assert.True(WebLoanOptions.IsValidDatabaseName(database));
    }

    [Theory]
    [InlineData("")]
    [InlineData("webloan.dbo")]
    [InlineData("webloan; DROP")]
    [InlineData("web loan")]
    public void Database_Invalid_rejected(string database)
    {
        Assert.False(WebLoanOptions.IsValidDatabaseName(database));
    }
}
