using Ebi.Alas.Api.Features.Approvals;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApprovalAuthorityTests
{
    [Fact]
    public void Create_DerivesSlugKeyAndTrimsDisplayName()
    {
        var authority = ApprovalAuthority.Create(
            "  Area Head  ", 2, 1, true, false, 1, 500_000m, 1);

        Assert.Equal("area-head", authority.Key);
        Assert.Equal("Area Head", authority.DisplayName);
        Assert.Equal(2, authority.Tier);
        Assert.Equal(1, authority.MaxSeverity);
        Assert.Equal(500_000m, authority.MaxTotalExposure);
        Assert.Equal(1, authority.ScopeType);
    }

    [Fact]
    public void Create_SlugifiesPunctuation()
    {
        var authority = ApprovalAuthority.Create(
            "Area Head (Level H)", 2, 0, true, true, 0, 0m, 0);

        Assert.Equal("area-head-level-h", authority.Key);
    }

    [Fact]
    public void Create_RejectsEmptyDisplayName()
    {
        Assert.Throws<ArgumentException>(() =>
            ApprovalAuthority.Create("   ", 1, 0, true, true, 0, 0m, 0));
    }

    [Fact]
    public void Create_RejectsTierBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ApprovalAuthority.Create("Branch Head", 0, 0, true, true, 0, 0m, 0));
    }

    [Fact]
    public void Create_RejectsSeverityOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ApprovalAuthority.Create("Branch Head", 1, 0, true, true, 3, 0m, 0));
    }

    [Fact]
    public void Create_RejectsNegativeExposure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ApprovalAuthority.Create("Branch Head", 1, 0, true, true, 0, -1m, 0));
    }

    [Fact]
    public void Create_RejectsScopeOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ApprovalAuthority.Create("Branch Head", 1, 0, true, true, 0, 0m, -1));
    }

    [Fact]
    public void Update_ChangesLimitsButKeepsKey()
    {
        var authority = ApprovalAuthority.Create(
            "Area Head", 2, 1, true, false, 1, 500_000m, 1);
        var key = authority.Key;

        authority.Update("Regional Head", 3, 2, false, true, 2, 1_000_000m, 2);

        Assert.Equal(key, authority.Key);
        Assert.Equal("Regional Head", authority.DisplayName);
        Assert.Equal(3, authority.Tier);
        Assert.Equal(2, authority.Priority);
        Assert.False(authority.AllowNew);
        Assert.True(authority.AllowRenewal);
        Assert.Equal(2, authority.MaxSeverity);
        Assert.Equal(1_000_000m, authority.MaxTotalExposure);
        Assert.Equal(2, authority.ScopeType);
    }
}
