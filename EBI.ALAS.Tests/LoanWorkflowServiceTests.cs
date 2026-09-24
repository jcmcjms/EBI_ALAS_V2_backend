using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.SystemSettings;
using Xunit;

namespace EBI.ALAS.Tests;

/// <summary>
/// Transition validation tests for LoanWorkflowService.
/// Focuses on the ForIncompleteDocuments edges — the flagged desk now
/// behaves identically to ForChecking for evaluator actions.
/// </summary>
public class LoanWorkflowServiceTests
{
    private sealed class StubConfig : IWorkflowConfiguration
    {
        public bool RequireRecommendation => true;
        public decimal MinimumNthp => 0;
        public string InitialStatus => "ForRecommendation";
        public IReadOnlyList<string> Stages => [];
        public string Source => "Test";
        public DateTime? UpdatedAt => null;
        public string? UpdatedByName => null;
        public void Apply(SettingSnapshot snapshot) { }
    }

    private readonly LoanWorkflowService _sut = new(new StubConfig());

    // ── ForIncompleteDocuments → ForRevision (new edge) ─────────────────

    [Theory]
    [InlineData(Roles.Evaluator)]
    public void IncompleteDocuments_ToRevision_Allowed_ForEvaluator(string role)
    {
        Assert.True(_sut.IsValidTransition("ForIncompleteDocuments", "ForRevision", role));
    }

    [Theory]
    [InlineData(Roles.Recommender)]
    [InlineData(Roles.Approver)]
    [InlineData(Roles.Encoder)]
    public void IncompleteDocuments_ToRevision_Denied_ForOtherRoles(string role)
    {
        Assert.False(_sut.IsValidTransition("ForIncompleteDocuments", "ForRevision", role));
    }

    [Fact]
    public void IncompleteDocuments_ToRevision_Allowed_ForAdmin()
    {
        // Admin can perform any transition
        Assert.True(_sut.IsValidTransition("ForIncompleteDocuments", "ForRevision", Roles.Admin));
    }

    // ── ForIncompleteDocuments → ForApproval (existing edge, still works) ──

    [Theory]
    [InlineData(Roles.Evaluator)]
    public void IncompleteDocuments_ToApproval_Allowed_ForEvaluator(string role)
    {
        Assert.True(_sut.IsValidTransition("ForIncompleteDocuments", "ForApproval", role));
    }

    [Theory]
    [InlineData(Roles.Recommender)]
    [InlineData(Roles.Approver)]
    [InlineData(Roles.Encoder)]
    public void IncompleteDocuments_ToApproval_Denied_ForOtherRoles(string role)
    {
        Assert.False(_sut.IsValidTransition("ForIncompleteDocuments", "ForApproval", role));
    }

    // ── ForIncompleteDocuments → ForChecking (encoder resubmit) ─────────

    [Theory]
    [InlineData(Roles.Encoder)]
    public void IncompleteDocuments_ToChecking_Allowed_ForEncoder(string role)
    {
        Assert.True(_sut.IsValidTransition("ForIncompleteDocuments", "ForChecking", role));
    }

    [Theory]
    [InlineData(Roles.Evaluator)]
    [InlineData(Roles.Recommender)]
    [InlineData(Roles.Approver)]
    public void IncompleteDocuments_ToChecking_Denied_ForOtherRoles(string role)
    {
        Assert.False(_sut.IsValidTransition("ForIncompleteDocuments", "ForChecking", role));
    }

    // ── ForIncompleteDocuments → Cancelled (encoder cancel) ─────────────

    [Theory]
    [InlineData(Roles.Encoder)]
    public void IncompleteDocuments_ToCancelled_Allowed_ForEncoder(string role)
    {
        Assert.True(_sut.IsValidTransition("ForIncompleteDocuments", "Cancelled", role));
    }

    // ── ForIncompleteDocuments → ForRecommendation (Admin escape hatch) ──

    [Theory]
    [InlineData(Roles.Admin)]
    public void IncompleteDocuments_ToRecommendation_Allowed_ForAdmin(string role)
    {
        Assert.True(_sut.IsValidTransition("ForIncompleteDocuments", "ForRecommendation", role));
    }

    [Theory]
    [InlineData(Roles.Evaluator)]
    [InlineData(Roles.Recommender)]
    [InlineData(Roles.Approver)]
    [InlineData(Roles.Encoder)]
    public void IncompleteDocuments_ToRecommendation_Denied_ForOtherRoles(string role)
    {
        Assert.False(_sut.IsValidTransition("ForIncompleteDocuments", "ForRecommendation", role));
    }

    // ── System actor: auto-return edges ─────────────────────────────────

    [Theory]
    [InlineData("ForChecking")]
    [InlineData("ForRecommendation")]
    [InlineData("ForApproval")]
    public void System_CanAutoReturnFromIncompleteDocuments(string target)
    {
        Assert.True(_sut.IsValidTransition("ForIncompleteDocuments", target, Roles.System));
    }

    [Fact]
    public void System_CannotPerformOtherTransitions()
    {
        Assert.False(_sut.IsValidTransition("ForChecking", "ForApproval", Roles.System));
        Assert.False(_sut.IsValidTransition("Draft", "ForRecommendation", Roles.System));
    }

    // ── ForChecking edges (regression guard — unchanged behavior) ───────

    [Theory]
    [InlineData(Roles.Evaluator)]
    public void Checking_ToApproval_Allowed_ForEvaluator(string role)
    {
        Assert.True(_sut.IsValidTransition("ForChecking", "ForApproval", role));
    }

    [Theory]
    [InlineData(Roles.Evaluator)]
    public void Checking_ToRevision_Allowed_ForEvaluator(string role)
    {
        Assert.True(_sut.IsValidTransition("ForChecking", "ForRevision", role));
    }

    [Theory]
    [InlineData(Roles.Evaluator)]
    public void Checking_ToIncompleteDocuments_Allowed_ForEvaluator(string role)
    {
        Assert.True(_sut.IsValidTransition("ForChecking", "ForIncompleteDocuments", role));
    }

    // ── GetRequiredRoleForTransition ────────────────────────────────────

    [Fact]
    public void GetRequiredRole_ReturnsEvaluator_ForIncompleteToRevision()
    {
        Assert.Equal(Roles.Evaluator,
            _sut.GetRequiredRoleForTransition("ForIncompleteDocuments", "ForRevision"));
    }

    [Fact]
    public void GetRequiredRole_ReturnsEvaluator_ForIncompleteToApproval()
    {
        Assert.Equal(Roles.Evaluator,
            _sut.GetRequiredRoleForTransition("ForIncompleteDocuments", "ForApproval"));
    }

    [Fact]
    public void GetRequiredRole_ReturnsEncoder_ForIncompleteToChecking()
    {
        Assert.Equal(Roles.Encoder,
            _sut.GetRequiredRoleForTransition("ForIncompleteDocuments", "ForChecking"));
    }

    // ── GetAllowedTransitions ───────────────────────────────────────────

    [Fact]
    public void GetAllowedTransitions_IncludesIncompleteDocuments_ForRevision()
    {
        var allowed = _sut.GetAllowedTransitions();
        Assert.True(allowed.ContainsKey("ForIncompleteDocuments"));
        Assert.Contains("ForRevision", allowed["ForIncompleteDocuments"]);
        Assert.Contains("ForApproval", allowed["ForIncompleteDocuments"]);
        Assert.Contains("ForChecking", allowed["ForIncompleteDocuments"]);
        Assert.Contains("Cancelled", allowed["ForIncompleteDocuments"]);
        Assert.Contains("ForRecommendation", allowed["ForIncompleteDocuments"]);
    }
}
