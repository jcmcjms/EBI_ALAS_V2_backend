namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Read model for a loan's position in its review desk queue.
/// </summary>
public record QueuePositionInfo(
    QueueStage Stage,
    int Position,
    int QueueLength,
    int? OwnerUserId,
    string? OwnerName,
    bool IsHead);

/// <summary>
/// Server-owned FIFO desk queue for loan review workflow.
/// Manages the lifecycle of queue items as loans move through
/// Recommendation → Evaluation → Approval stages.
/// </summary>
public interface IWorkflowQueueService
{
    /// <summary>
    /// After status persist: loan entered a review desk.
    /// Enqueues the loan and promotes the head if the desk is free.
    /// </summary>
    Task EnqueueAsync(LoanApplication loan, string newStatus, CancellationToken ct);

    /// <summary>
    /// After status persist: loan left a review desk (progress, pushback, cancel).
    /// Dequeues the loan and promotes the next item in the partition.
    /// </summary>
    Task DequeueAndPromoteAsync(LoanApplication loan, string oldStatus, CancellationToken ct);

    /// <summary>
    /// Read model for a page of loan ids (single query, rank computed in memory).
    /// </summary>
    Task<IReadOnlyDictionary<int, QueuePositionInfo>> GetPositionsAsync(
        IReadOnlyCollection<int> loanIds, CancellationToken ct);

    /// <summary>
    /// Ownership guard for review transitions. Returns true when the user
    /// is the head owner of the loan's current review desk.
    /// </summary>
    Task<bool> IsHeadOwnerAsync(int loanId, int userId, string currentStatus, CancellationToken ct);
}
