namespace Ebi.Alas.Api.Features.Workflow;

public sealed class WorkflowQueueItem
{
    private WorkflowQueueItem()
    {
        PartitionKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public WorkflowStage Stage { get; private set; }

    public QueueItemState State { get; private set; }

    public string PartitionKey { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public DateTimeOffset? LeasedAt { get; private set; }

    public TimeSpan LeaseDuration { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }

    public int Sequence { get; private set; }

    public static WorkflowQueueItem Enqueue(
        Guid loanApplicationId,
        WorkflowStage stage,
        string partitionKey,
        int sequence,
        TimeSpan leaseDuration,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        return new WorkflowQueueItem
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            Stage = stage,
            State = QueueItemState.Queued,
            PartitionKey = partitionKey,
            LeaseDuration = leaseDuration,
            QueuedAt = now,
            Sequence = sequence
        };
    }

    public bool TryClaim(Guid userId, DateTimeOffset now)
    {
        if (State == QueueItemState.Active && !IsLeaseExpired(now))
        {
            return false;
        }

        State = QueueItemState.Active;
        OwnerUserId = userId;
        LeasedAt = now;
        return true;
    }

    public bool Release(Guid userId, DateTimeOffset now)
    {
        if (OwnerUserId != userId)
        {
            return false;
        }

        State = QueueItemState.Queued;
        OwnerUserId = null;
        LeasedAt = null;
        return true;
    }

    public void Complete(Guid userId, DateTimeOffset now)
    {
        if (OwnerUserId != userId)
        {
            throw new InvalidOperationException("Only the lease owner can complete the item.");
        }

        State = QueueItemState.Completed;
        LeasedAt = now;
    }

    public bool IsLeaseExpired(DateTimeOffset now) =>
        State == QueueItemState.Active
        && LeasedAt is { } leased
        && now - leased > LeaseDuration;

    public void ReapExpiredLease(DateTimeOffset now)
    {
        if (!IsLeaseExpired(now))
        {
            return;
        }

        State = QueueItemState.Queued;
        OwnerUserId = null;
        LeasedAt = null;
    }
}
