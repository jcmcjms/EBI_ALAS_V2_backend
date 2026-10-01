using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Api.Features.Loans;
public enum QueueStage
{
    Recommendation,
    Evaluation,
    Approval,
}
public enum QueueItemState
{
    Queued,
    Active,
    Completed,
}
public class WorkflowQueueItem
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;
    public QueueStage Stage { get; set; }
    public string PartitionKey { get; set; } = string.Empty;
    public DateTime EnqueuedAt { get; set; }
    public QueueItemState State { get; set; } = QueueItemState.Queued;
    public int? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }
    public DateTime? PromotedAt { get; set; }
    public DateTime? LeasedAt { get; set; }
    public DateTime? DequeuedAt { get; set; }
}
