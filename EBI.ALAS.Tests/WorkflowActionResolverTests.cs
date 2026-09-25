using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.SystemSettings;
using Xunit;

namespace EBI.ALAS.Tests;

public class WorkflowActionResolverTests
{
    private sealed class FakeWorkflowConfiguration : IWorkflowConfiguration
    {
        public bool RequireRecommendation => true;
        public decimal MinimumNthp => 5_800m;
        public string InitialStatus => "ForRecommendation";
        public IReadOnlyList<string> Stages => new[] { "ForRecommendation", "ForChecking", "ForApproval" };
        public string Source => "AppConfig";
        public DateTime? UpdatedAt => null;
        public string? UpdatedByName => null;
        public void Apply(SettingSnapshot snapshot) { }
    }

    [Fact]
    public void Recommend_FromForRecommendation_ReturnsForChecking()
    {
        var service = new LoanWorkflowService(new FakeWorkflowConfiguration());
        var result = service.ResolveAction(WorkflowAction.Recommend, "ForRecommendation");
        Assert.Equal("ForChecking", result.TargetStatus);
        Assert.Null(result.Verdict);
    }

    [Fact]
    public void Recommend_FromForChecking_ReturnsForApprovalWithRecommendedVerdict()
    {
        var service = new LoanWorkflowService(new FakeWorkflowConfiguration());
        var result = service.ResolveAction(WorkflowAction.Recommend, "ForChecking");
        Assert.Equal("ForApproval", result.TargetStatus);
        Assert.Equal("Recommended", result.Verdict);
    }

    [Fact]
    public void NotRecommend_FromForChecking_ReturnsForApprovalWithNotRecommendedVerdict()
    {
        var service = new LoanWorkflowService(new FakeWorkflowConfiguration());
        var result = service.ResolveAction(WorkflowAction.NotRecommend, "ForChecking");
        Assert.Equal("ForApproval", result.TargetStatus);
        Assert.Equal("NotRecommended", result.Verdict);
    }

    [Fact]
    public void UnsupportedAction_Throws()
    {
        var service = new LoanWorkflowService(new FakeWorkflowConfiguration());
        Assert.Throws<InvalidOperationException>(() => service.ResolveAction(WorkflowAction.Approve, "ForChecking"));
    }
}