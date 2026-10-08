using Ebi.Alas.Api.Features.Workflow;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

public sealed class WorkflowQueueItemTests
{
    [Fact]
    public void TryClaim_QueuedItem_Succeeds()
    {
        var item = WorkflowQueueItem.Enqueue(
            Guid.NewGuid(), WorkflowStage.Recommendation, "011", 1, TimeSpan.FromMinutes(30), DateTimeOffset.UtcNow);

        Assert.True(item.TryClaim(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Equal(QueueItemState.Active, item.State);
    }

    [Fact]
    public void TryClaim_ActiveUnexpiredItem_Fails()
    {
        var now = DateTimeOffset.UtcNow;
        var item = WorkflowQueueItem.Enqueue(Guid.NewGuid(), WorkflowStage.Recommendation, "011", 1, TimeSpan.FromMinutes(30), now);
        item.TryClaim(Guid.NewGuid(), now);

        Assert.False(item.TryClaim(Guid.NewGuid(), now.AddMinutes(5)));
    }

    [Fact]
    public void TryClaim_ActiveExpiredItem_Succeeds()
    {
        var now = DateTimeOffset.UtcNow;
        var item = WorkflowQueueItem.Enqueue(Guid.NewGuid(), WorkflowStage.Recommendation, "011", 1, TimeSpan.FromMinutes(30), now);
        item.TryClaim(Guid.NewGuid(), now);

        Assert.True(item.TryClaim(Guid.NewGuid(), now.AddMinutes(45)));
    }

    [Fact]
    public void Release_NonOwner_Fails()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid();
        var item = WorkflowQueueItem.Enqueue(Guid.NewGuid(), WorkflowStage.Recommendation, "011", 1, TimeSpan.FromMinutes(30), now);
        item.TryClaim(owner, now);

        Assert.False(item.Release(Guid.NewGuid(), now));
    }
}
