using EBI.ALAS.Api.Features.Loans;
using Xunit;
namespace EBI.ALAS.Tests;
public class WorkflowQueueServiceTests
{
    [Fact]
    public void StageForStatus_ForIncompleteDocuments_ReturnsNull()
    {
        Assert.Null(WorkflowQueueService.StageForStatus("ForIncompleteDocuments"));
    }
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
