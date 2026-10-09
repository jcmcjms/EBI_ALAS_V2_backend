using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

public sealed class WorkflowQueueReapBoundTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"reap-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task ReapExpiredAsync_ProcessesAtMostBatchSizeItems()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var service = new WorkflowQueueService(db, TimeProvider.System);

        for (var i = 0; i < WorkflowQueueService.ReapBatchSize + 25; i++)
        {
            var item = WorkflowQueueItem.Enqueue(
                Guid.NewGuid(),
                WorkflowStage.Recommendation,
                "011",
                i + 1,
                WorkflowQueueService.DefaultLease,
                now.AddMinutes(-45));
            Assert.True(item.TryClaim(Guid.NewGuid(), now.AddMinutes(-45)));
            db.WorkflowQueueItems.Add(item);
        }

        await db.SaveChangesAsync();

        var reaped = await service.ReapExpiredAsync(
            WorkflowStage.Recommendation,
            "011",
            CancellationToken.None);

        Assert.Equal(WorkflowQueueService.ReapBatchSize, reaped);
        Assert.True(await db.WorkflowQueueItems.CountAsync() > WorkflowQueueService.ReapBatchSize);
    }
}
