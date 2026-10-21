using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApprovalMatrixTests
{
    private static ApprovalInputs Input(
        LoanType type,
        int severity,
        decimal exposure,
        bool ratePrice = false) =>
        new(type, severity, exposure, ratePrice);

    [Theory]
    [InlineData(300_000, AuthorityLevel.BranchHead)]
    [InlineData(300_001, AuthorityLevel.AreaHead)]
    [InlineData(600_000, AuthorityLevel.AreaHead)]
    [InlineData(600_001, AuthorityLevel.RbgProductCredit)]
    [InlineData(1_000_000, AuthorityLevel.RbgProductCredit)]
    [InlineData(1_000_001, AuthorityLevel.PresidentCoo)]
    [InlineData(1_500_000, AuthorityLevel.PresidentCoo)]
    [InlineData(1_500_001, AuthorityLevel.CreComBod)]
    public void Reloan_NoDeviation_EscalatesByExposure(decimal exposure, AuthorityLevel expected)
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.Reloan, 0, exposure));
        Assert.Equal(expected, decision.RequiredLevel);
    }

    [Theory]
    [InlineData(500_000, AuthorityLevel.RbgProductCredit)]
    [InlineData(1_000_000, AuthorityLevel.RbgProductCredit)]
    [InlineData(1_000_001, AuthorityLevel.PresidentCoo)]
    public void New_NoDeviation_UsesRbgNotBranch(decimal exposure, AuthorityLevel expected)
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 0, exposure));
        Assert.Equal(expected, decision.RequiredLevel);
    }

    [Theory]
    [InlineData(LoanType.New)]
    [InlineData(LoanType.Reloan)]
    public void MinorDeviation_Upto1M_IsRbg(LoanType type)
    {
        var decision = ApprovalMatrix.Resolve(Input(type, 1, 900_000m));
        Assert.Equal(AuthorityLevel.RbgProductCredit, decision.RequiredLevel);
    }

    [Fact]
    public void MinorDeviation_Above1M_IsPresidentCoo()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 1, 1_200_000m));
        Assert.Equal(AuthorityLevel.PresidentCoo, decision.RequiredLevel);
    }

    [Fact]
    public void MajorDeviation_Upto1_5M_IsPresidentCoo()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 2, 1_400_000m));
        Assert.Equal(AuthorityLevel.PresidentCoo, decision.RequiredLevel);
    }

    [Fact]
    public void MajorDeviation_Above1_5M_IsCreCom()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 2, 1_500_001m));
        Assert.Equal(AuthorityLevel.CreComBod, decision.RequiredLevel);
        Assert.NotNull(decision.NoAuthorityReason);
    }

    [Fact]
    public void RatePriceDeviation_IsCreCom_EvenBelow1_5M()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 2, 800_000m, ratePrice: true));
        Assert.Equal(AuthorityLevel.CreComBod, decision.RequiredLevel);
        Assert.NotNull(decision.NoAuthorityReason);
    }

    [Theory]
    [InlineData("Interest rate reduction", true)]
    [InlineData("Request of Interest Rate at 9%", true)]
    [InlineData("Lacking CIBI", false)]
    public void IsRatePriceDeviation_MatchesCatalogDescriptions(string description, bool expected)
        => Assert.Equal(expected, ApprovalMatrix.IsRatePriceDeviation(description));

    [Fact]
    public void Describe_ReturnsBoardFacingLabel()
    {
        Assert.Equal("Branch Head / OIC", ApprovalMatrix.Describe(AuthorityLevel.BranchHead));
        Assert.Equal("CreCom / Board", ApprovalMatrix.Describe(AuthorityLevel.CreComBod));
    }
}
