using EBI.ALAS.Api.Features.ApprovalMatrix;
using Xunit;

namespace EBI.ALAS.Tests;

public class ApprovalCycleResolverTests
{
    // Build a minimal authority list matching the seeded ladder.
    private static List<ApprovalAuthority> Ladder => new()
    {
        new() { Key = "BranchHead",   DisplayName = "Branch Head / OIC Level 1",              Tier = 1, Priority = 1, AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,   MaxTotalExposure = 300_000m,    ScopeType = AuthorityScope.Branch },
        new() { Key = "OICLevel1",    DisplayName = "Branch Head / OIC Level 1",              Tier = 1, Priority = 2, AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,   MaxTotalExposure = 300_000m,    ScopeType = AuthorityScope.Branch },
        new() { Key = "AreaHead",     DisplayName = "Area Head",                               Tier = 2, Priority = 1, AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None,   MaxTotalExposure = 600_000m,    ScopeType = AuthorityScope.Area },
        new() { Key = "RBGHead",      DisplayName = "RBG Head / Product Head / Credit Head",   Tier = 3, Priority = 1, AllowNew = true,  AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,  MaxTotalExposure = 1_000_000m,  ScopeType = AuthorityScope.Global },
        new() { Key = "ProductHead",  DisplayName = "RBG Head / Product Head / Credit Head",   Tier = 3, Priority = 2, AllowNew = true,  AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,  MaxTotalExposure = 1_000_000m,  ScopeType = AuthorityScope.Global },
        new() { Key = "CreditHead",   DisplayName = "RBG Head / Product Head / Credit Head",   Tier = 3, Priority = 3, AllowNew = true,  AllowRenewal = true, MaxSeverity = DeviationSeverity.Minor,  MaxTotalExposure = 1_000_000m,  ScopeType = AuthorityScope.Global },
        new() { Key = "COO",          DisplayName = "COO",                                     Tier = 4, Priority = 1, AllowNew = true,  AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,  MaxTotalExposure = 1_200_000m,  ScopeType = AuthorityScope.Global },
        new() { Key = "CEOPresident", DisplayName = "CEO / President / CreCom Chair Level D",  Tier = 5, Priority = 1, AllowNew = true,  AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,  MaxTotalExposure = 1_500_000m,  ScopeType = AuthorityScope.Global },
        new() { Key = "CreComChair",  DisplayName = "CEO / President / CreCom Chair Level D",  Tier = 5, Priority = 2, AllowNew = true,  AllowRenewal = true, MaxSeverity = DeviationSeverity.Major,  MaxTotalExposure = 1_500_000m,  ScopeType = AuthorityScope.Global },
    };

    // ── Boundary tests (inclusive caps) ────────────────────────────────

    [Fact]
    public void Boundary_300k_Inclusive_ReturnsTier1()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.None, 300_000m));
        Assert.NotNull(result);
        Assert.Equal(1, result!.Tier);
    }

    [Fact]
    public void Boundary_600k_Inclusive_ReturnsTier2()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.None, 600_000m));
        Assert.NotNull(result);
        Assert.Equal(2, result!.Tier);
    }

    [Fact]
    public void Boundary_1M_Inclusive_ReturnsTier3()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Minor, 1_000_000m));
        Assert.NotNull(result);
        Assert.Equal(3, result!.Tier);
    }

    [Fact]
    public void Boundary_1200k_Inclusive_ReturnsTier4()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Major, 1_200_000m));
        Assert.NotNull(result);
        Assert.Equal(4, result!.Tier);
    }

    [Fact]
    public void Boundary_1500k_Inclusive_ReturnsTier5()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Major, 1_500_000m));
        Assert.NotNull(result);
        Assert.Equal(5, result!.Tier);
    }

    // ── Exact spec cases ───────────────────────────────────────────────

    [Fact]
    public void Renewal_None_300k_ReturnsTier1()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.None, 300_000m));
        Assert.NotNull(result);
        Assert.Equal(1, result!.Tier);
    }

    [Fact]
    public void Renewal_None_300001_ReturnsTier2()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.None, 300_001m));
        Assert.NotNull(result);
        Assert.Equal(2, result!.Tier);
    }

    [Fact]
    public void Renewal_None_489k_ReturnsTier2()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.None, 489_000m));
        Assert.NotNull(result);
        Assert.Equal(2, result!.Tier);
    }

    [Fact]
    public void Renewal_None_600k_ReturnsTier2()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.None, 600_000m));
        Assert.NotNull(result);
        Assert.Equal(2, result!.Tier);
    }

    [Fact]
    public void Renewal_None_600001_ReturnsTier3()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.None, 600_001m));
        Assert.NotNull(result);
        Assert.Equal(3, result!.Tier);
    }

    [Fact]
    public void New_Minor_1M_ReturnsTier3()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Minor, 1_000_000m));
        Assert.NotNull(result);
        Assert.Equal(3, result!.Tier);
    }

    [Fact]
    public void Any_Minor_1000001_EscalatesToTier4()
    {
        // Minor above 1M has no dedicated tier, but COO (Tier 4) allows
        // Major which is >= Minor, so the resolver escalates there.
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Minor, 1_000_001m));
        Assert.NotNull(result);
        Assert.Equal(4, result!.Tier);
    }

    [Fact]
    public void Any_Major_1200k_ReturnsTier4()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Major, 1_200_000m));
        Assert.NotNull(result);
        Assert.Equal(4, result!.Tier);
    }

    [Fact]
    public void Any_Major_1500k_ReturnsTier5()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.Major, 1_500_000m));
        Assert.NotNull(result);
        Assert.Equal(5, result!.Tier);
    }

    [Fact]
    public void Any_Major_1500001_ReturnsNull()
    {
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Major, 1_500_001m));
        Assert.Null(result);
    }

    // ── Cycle routing ──────────────────────────────────────────────────

    [Fact]
    public void New_None_300k_ReturnsTier3()
    {
        // New loans skip Tier 1 and 2 (AllowNew=false)
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.None, 300_000m));
        Assert.NotNull(result);
        Assert.Equal(3, result!.Tier);
    }

    [Fact]
    public void Match_ReturnsNull_WhenCycleNotAllowed()
    {
        var renewalOnly = new List<ApprovalAuthority>
        {
            new() { Key = "BH", DisplayName = "Branch Head", Tier = 1, Priority = 1, AllowNew = false, AllowRenewal = true, MaxSeverity = DeviationSeverity.None, MaxTotalExposure = 300_000m, ScopeType = AuthorityScope.Branch },
        };
        var result = ApprovalCycleResolver.Match(renewalOnly, new(LoanCycle.New, DeviationSeverity.None, 100_000m));
        Assert.Null(result);
    }

    [Fact]
    public void Match_Major_SkipsTiers1to3_ForRenewal()
    {
        // Major deviation skips Tier 1-3 (MaxSeverity too low), lands on Tier 4
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.Renewal, DeviationSeverity.Major, 100_000m));
        Assert.NotNull(result);
        Assert.Equal(4, result!.Tier);
    }

    [Fact]
    public void Match_ReturnsFirstAuthorityInTier()
    {
        // Tier 3 has 3 authorities; should return RBGHead (Priority=1)
        var result = ApprovalCycleResolver.Match(Ladder, new(LoanCycle.New, DeviationSeverity.Minor, 500_000m));
        Assert.NotNull(result);
        Assert.Equal(3, result!.Tier);
        Assert.Equal("RBGHead", result!.Key);
    }
}