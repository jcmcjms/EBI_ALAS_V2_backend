using Ebi.Alas.Api.Features.Approvals;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class AuthorityJobTitlesTests
{
    [Theory]
    [InlineData("Branch Head", AuthorityLevel.BranchHead)]
    [InlineData("Branch Manager", AuthorityLevel.BranchHead)]
    [InlineData("Senior Branch Head", AuthorityLevel.BranchHead)]
    [InlineData("Officer in Charge", AuthorityLevel.OfficerInCharge)]
    [InlineData("Branch OIC", AuthorityLevel.OfficerInCharge)]
    [InlineData("OIC", AuthorityLevel.OfficerInCharge)]
    [InlineData("Area Head", AuthorityLevel.AreaHead)]
    [InlineData("Area Head Level H", AuthorityLevel.AreaHead)]
    [InlineData("RBG Head", AuthorityLevel.RbgHead)]
    [InlineData("Product Head", AuthorityLevel.ProductHead)]
    [InlineData("Credit Head", AuthorityLevel.CreditHead)]
    [InlineData("President & CEO", AuthorityLevel.PresidentCeo)]
    [InlineData("CEO/President", AuthorityLevel.PresidentCeo)]
    [InlineData("COO", AuthorityLevel.Coo)]
    [InlineData("Chief Operating Officer", AuthorityLevel.Coo)]
    [InlineData("CreCom Chair", AuthorityLevel.PresidentCeo)]
    [InlineData("BOD Level A", AuthorityLevel.CreComBod)]
    [InlineData("Board Credit Committee", AuthorityLevel.CreComBod)]
    public void Map_KnownTitles_ReturnsLevel(string title, AuthorityLevel expected)
        => Assert.Equal(expected, AuthorityJobTitles.Map(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Account Officer")]
    [InlineData("Credit Checker")]
    public void Map_Unknown_ReturnsNull(string? title)
        => Assert.Null(AuthorityJobTitles.Map(title));

    [Fact]
    public void Compact_StripsNonAlphanumeric()
        => Assert.Equal("AreaHeadLevelH", AuthorityJobTitles.Compact("Area Head Level H"));
}
