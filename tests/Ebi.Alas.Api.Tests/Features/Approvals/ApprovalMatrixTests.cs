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
    [InlineData(600_001, AuthorityLevel.RbgHead)]
    [InlineData(1_000_000, AuthorityLevel.RbgHead)]
    [InlineData(1_000_001, AuthorityLevel.Coo)]
    [InlineData(1_200_000, AuthorityLevel.Coo)]
    [InlineData(1_200_001, AuthorityLevel.PresidentCeo)]
    public void Reloan_NoDeviation_EscalatesByExposure(decimal exposure, AuthorityLevel expected)
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.Reloan, 0, exposure));
        Assert.Equal(expected, decision.RequiredLevel);
    }

    [Theory]
    [InlineData(500_000, AuthorityLevel.RbgHead)]
    [InlineData(1_000_000, AuthorityLevel.RbgHead)]
    [InlineData(1_000_001, AuthorityLevel.Coo)]
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
        Assert.Equal(AuthorityLevel.RbgHead, decision.RequiredLevel);
    }

    [Fact]
    public void MinorDeviation_Above1M_IsCoo()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 1, 1_200_000m));
        Assert.Equal(AuthorityLevel.Coo, decision.RequiredLevel);
    }

    [Fact]
    public void MajorDeviation_Upto1_2M_IsCoo()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 2, 1_100_000m));
        Assert.Equal(AuthorityLevel.Coo, decision.RequiredLevel);
    }

    [Fact]
    public void MajorDeviation_Above1_2M_Upto1_5M_IsPresident()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 2, 1_200_001m));
        Assert.Equal(AuthorityLevel.PresidentCeo, decision.RequiredLevel);
        Assert.Null(decision.NoAuthorityReason);
    }

    [Fact]
    public void MajorDeviation_Above1_5M_IsCreCom()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 2, 1_500_001m));
        Assert.Equal(AuthorityLevel.CreComBod, decision.RequiredLevel);
        Assert.NotNull(decision.NoAuthorityReason);
    }

    [Theory]
    [InlineData(LoanType.Reloan, 805_000, AuthorityLevel.Coo)]
    [InlineData(LoanType.New, 800_000, AuthorityLevel.Coo)]
    [InlineData(LoanType.New, 1_200_000, AuthorityLevel.Coo)]
    [InlineData(LoanType.New, 1_400_000, AuthorityLevel.PresidentCeo)]
    public void RatePriceDeviation_Below1_5M_UsesMajorLadder(LoanType type, decimal exposure, AuthorityLevel expected)
    {
        var decision = ApprovalMatrix.Resolve(Input(type, 2, exposure, ratePrice: true));
        Assert.Equal(expected, decision.RequiredLevel);
        Assert.Null(decision.NoAuthorityReason);
    }

    [Fact]
    public void RatePriceDeviation_Above1_5M_IsCreCom()
    {
        var decision = ApprovalMatrix.Resolve(Input(LoanType.New, 2, 1_500_001m, ratePrice: true));
        Assert.Equal(AuthorityLevel.CreComBod, decision.RequiredLevel);
        Assert.NotNull(decision.NoAuthorityReason);
    }

    [Fact]
    public void Reloan_InterestRateReduction_805k_IsCooNotCreCom()
    {
        // Authority Level Consumer Loan + Ammended Active Deviation:
        // Interest rate reduction is Major; rate/price is CreCom only above ₱1.5M.
        var decision = ApprovalMatrix.Resolve(Input(LoanType.Reloan, 2, 805_000m, ratePrice: true));
        Assert.Equal(AuthorityLevel.Coo, decision.RequiredLevel);
        Assert.Null(decision.NoAuthorityReason);
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
        Assert.Equal("Branch Head", ApprovalMatrix.Describe(AuthorityLevel.BranchHead));
        Assert.Equal("Officer In Charge", ApprovalMatrix.Describe(AuthorityLevel.OfficerInCharge));
        Assert.Equal("Area Head", ApprovalMatrix.Describe(AuthorityLevel.AreaHead));
        Assert.Equal("RBG Head", ApprovalMatrix.Describe(AuthorityLevel.RbgHead));
        Assert.Equal("Product Head", ApprovalMatrix.Describe(AuthorityLevel.ProductHead));
        Assert.Equal("Credit Head", ApprovalMatrix.Describe(AuthorityLevel.CreditHead));
        Assert.Equal("COO", ApprovalMatrix.Describe(AuthorityLevel.Coo));
        Assert.Equal("President / CEO", ApprovalMatrix.Describe(AuthorityLevel.PresidentCeo));
        Assert.Equal("CreCom / Board", ApprovalMatrix.Describe(AuthorityLevel.CreComBod));
    }
}
