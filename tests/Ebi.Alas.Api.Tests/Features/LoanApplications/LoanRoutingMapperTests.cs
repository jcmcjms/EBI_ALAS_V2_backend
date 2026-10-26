using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanRoutingMapperTests
{
    private static LoanApplication Reloan(decimal principal, string branch = "011")
    {
        var loan = LoanApplication.Create(
            "LAM-R", "G-R", "Client", branch, LoanType.Reloan,
            principal, 30, 0.05m, 10m, 50m, principal - 60m,
            Guid.NewGuid(), DateTimeOffset.UtcNow, "A16", "A16");
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        loan.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        return loan;
    }

    [Fact]
    public void Build_SmallReloan_UsesAssignedApproverNotLastActor()
    {
        var assignee = Guid.NewGuid();
        var lastActor = Guid.NewGuid();
        var loan = Reloan(100_000m);
        loan.AssignTo(assignee, DateTimeOffset.UtcNow);

        var dto = LoanRoutingMapper.Build(
            loan,
            maxSeverity: 0,
            hasRatePriceDeviation: false,
            missingDocuments: [],
            assignedApproverName: "Ana Approver");

        Assert.Equal(1, dto.RequiredApprovalTier);
        Assert.Equal(assignee, dto.AssignedApproverId);
        Assert.NotEqual(lastActor, dto.AssignedApproverId);
        Assert.Equal("Ana Approver", dto.AssignedApproverName);
        Assert.Equal("Branch Head", dto.MatchedRule);
        Assert.Null(dto.NoAuthorityReason);
        Assert.Equal("Renewal", dto.Evaluated!.Cycle);
    }

    [Fact]
    public void Build_CreCom_HasReasonAndTier9()
    {
        var loan = Reloan(2_000_000m);

        var dto = LoanRoutingMapper.Build(
            loan,
            maxSeverity: 2,
            hasRatePriceDeviation: false,
            missingDocuments: ["SOA"],
            assignedApproverName: null);

        Assert.Equal(9, dto.RequiredApprovalTier);
        Assert.Null(dto.AssignedApproverId);
        Assert.NotNull(dto.NoAuthorityReason);
        Assert.False(dto.DocumentsComplete);
        Assert.Equal("Major", dto.Evaluated!.Severity);
    }

    [Fact]
    public void Build_RatePrice_Below1_5M_IsCooNotCreCom()
    {
        var loan = Reloan(800_000m);

        var dto = LoanRoutingMapper.Build(
            loan,
            maxSeverity: 2,
            hasRatePriceDeviation: true,
            missingDocuments: [],
            assignedApproverName: null);

        Assert.Equal((int)AuthorityLevel.Coo, dto.RequiredApprovalTier);
        Assert.Null(dto.NoAuthorityReason);
    }

    [Fact]
    public void Build_RatePrice_Above1_5M_IsCreCom()
    {
        var loan = Reloan(1_600_000m);

        var dto = LoanRoutingMapper.Build(
            loan,
            maxSeverity: 2,
            hasRatePriceDeviation: true,
            missingDocuments: [],
            assignedApproverName: null);

        Assert.Equal((int)AuthorityLevel.CreComBod, dto.RequiredApprovalTier);
        Assert.NotNull(dto.NoAuthorityReason);
    }
}
