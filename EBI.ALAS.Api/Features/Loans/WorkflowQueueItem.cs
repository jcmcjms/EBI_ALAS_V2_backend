using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Materialized head of a FIFO review desk. Each in-flight loan occupies
/// exactly one row per review stage (Queued or Active). Completed rows
/// remain for audit — tenure is queryable via (EnqueuedAt → DequeuedAt).
///
/// PartitionKey encodes the desk identity:
///   Recommendation → "REC:{branchCode}"
///   Evaluation     → "EVA:{branchCode}"
///   Approval       → "APP:{branchCode}:{tier}"
///
/// FIFO order = (EnqueuedAt, Id) — immutable, so order can't drift.
/// Exactly one Active item per partition = the file currently on the desk.
/// </summary>
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

    /// <summary>
    /// Desk partition: "REC:{branch}" | "EVA:{branch}" | "APP:{branch}:{tier}".
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>Immutable enqueue timestamp — FIFO ordering column.</summary>
    public DateTime EnqueuedAt { get; set; }

    public QueueItemState State { get; set; } = QueueItemState.Queued;

    /// <summary>
    /// Materialized only for the head (State == Active).
    /// Null when no officer is assigned (desk has zero candidates).
    /// </summary>
    public int? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }

    /// <summary>Timestamp when this item was promoted to Active.</summary>
    public DateTime? PromotedAt { get; set; }

    /// <summary>Timestamp when this item was dequeued (status transition or cancel).</summary>
    public DateTime? DequeuedAt { get; set; }
}
