using Ebi.Alas.Api.Features.Approvals;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class AuthorityJobTitlesTests
{
    [Theory]
    [InlineData("Branch Head", AuthorityLevel.BranchHead)]
    [InlineData("Branch Manager", AuthorityLevel.BranchHead)]
    [InlineData("Senior Branch Head", AuthorityLevel.BranchHead)]
    [InlineData("Officer in Charge", AuthorityLevel.BranchHead)]
    [InlineData("Branch OIC", AuthorityLevel.BranchHead)]
    [InlineData("OIC", AuthorityLevel.BranchHead)]
    [InlineData("Area Head", AuthorityLevel.AreaHead)]
    [InlineData("Area Head Level H", AuthorityLevel.AreaHead)]
    [InlineData("RBG Head", AuthorityLevel.RbgProductCredit)]
    [InlineData("Product Head", AuthorityLevel.RbgProductCredit)]
    [InlineData("Credit Head", AuthorityLevel.RbgProductCredit)]
    [InlineData("President & CEO", AuthorityLevel.PresidentCoo)]
    [InlineData("CEO/President", AuthorityLevel.PresidentCoo)]
    [InlineData("COO", AuthorityLevel.PresidentCoo)]
    [InlineData("CreCom Chair", AuthorityLevel.PresidentCoo)]
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
