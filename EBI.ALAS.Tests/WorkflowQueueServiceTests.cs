using EBI.ALAS.Api.Features.Loans;
using Xunit;

namespace EBI.ALAS.Tests;

/// <summary>
/// Tests for WorkflowQueueService.StageForStatus — the static method that
/// maps workflow statuses to queue stages. ForIncompleteDocuments must return
/// null (tracking state, not a turn-based desk).
/// </summary>
public class WorkflowQueueServiceTests
{
    // ── ForIncompleteDocuments → null (tracking state, no queue) ──────────

    [Fact]
    public void StageForStatus_ForIncompleteDocuments_ReturnsNull()
    {
        Assert.Null(WorkflowQueueService.StageForStatus("ForIncompleteDocuments"));
    }

    // ── Real desks → correct stages (regression guards) ───────────────────

    [Fact]
    public void StageForStatus_ForRecommendation_ReturnsRecommendation()
    {
        Assert.Equal(QueueStage.Recommendation, WorkflowQueueService.StageForStatus("ForRecommendation"));
    }

    [Fact]
    public void StageForStatus_ForChecking_ReturnsEvaluation()
    {
        Assert.Equal(QueueStage.Evaluation, WorkflowQueueService.StageForStatus("ForChecking"));
    }

    [Fact]
    public void StageForStatus_ForApproval_ReturnsApproval()
    {
        Assert.Equal(QueueStage.Approval, WorkflowQueueService.StageForStatus("ForApproval"));
    }

    // ── Non-desk statuses → null ──────────────────────────────────────────

    [Theory]
    [InlineData("Draft")]
    [InlineData("ForRevision")]
    [InlineData("Approved")]
    [InlineData("Cancelled")]
    public void StageForStatus_NonDeskStatuses_ReturnNull(string status)
    {
        Assert.Null(WorkflowQueueService.StageForStatus(status));
    }
}
