using Ebi.Alas.Api.Features.Approvals;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApprovalRoutingServiceTests
{
    private readonly ApprovalRoutingService _service = new();

    [Fact]
    public void Route_LowExposure_SelectsTier1()
    {
        var authorities = new[]
        {
            new ApprovalAuthority(Guid.NewGuid(), 1, true, true, 1, 100_000m, ApprovalScope.Global, "*"),
            new ApprovalAuthority(Guid.NewGuid(), 3, true, true, 5, 5_000_000m, ApprovalScope.Global, "*")
        };

        var decision = _service.Route(new RoutingRequest(50_000m, false, 0, "011", null), authorities);

        Assert.Equal(1, decision.RequiredTier);
        Assert.False(decision.IsEscalated);
    }

    [Fact]
    public void Route_HighExposure_RequiresTier3()
    {
        var authorities = new[]
        {
            new ApprovalAuthority(Guid.NewGuid(), 1, true, true, 1, 100_000m, ApprovalScope.Global, "*")
        };

        var decision = _service.Route(new RoutingRequest(2_000_000m, false, 0, "011", null), authorities);

        Assert.Equal(3, decision.RequiredTier);
        Assert.True(decision.IsEscalated);
        Assert.Null(decision.MatchedAuthorityId);
    }

    [Fact]
    public void Route_BranchScope_DoesNotMatchOtherBranch()
    {
        var authorities = new[]
        {
            new ApprovalAuthority(Guid.NewGuid(), 1, true, true, 5, 5_000_000m, ApprovalScope.Branch, "012")
        };

        var decision = _service.Route(new RoutingRequest(10_000m, false, 0, "011", null), authorities);

        Assert.Null(decision.MatchedAuthorityId);
        Assert.True(decision.IsEscalated);
    }
}
