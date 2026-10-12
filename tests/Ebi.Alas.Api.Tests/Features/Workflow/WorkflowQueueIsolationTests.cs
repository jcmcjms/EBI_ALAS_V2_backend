using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

public sealed class WorkflowQueueIsolationTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"queue-iso-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task SeedAsync(AlasDbContext db, string partitionKey, int count, DateTimeOffset now)
    {
        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        for (var i = 0; i < count; i++)
        {
            await queue.EnqueueAsync(Guid.NewGuid(), WorkflowStage.Recommendation, partitionKey, CancellationToken.None);
        }
    }

    [Fact]
    public void ClaimQueueRequest_DoesNotExposeClientControlledPartitionKey()
    {
        var properties = typeof(ClaimQueueRequest).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name == "PartitionKey");
        Assert.Contains(properties, p => p.Name == "Stage");
    }

    [Fact]
    public void ResolvePartition_NonAdmin_IgnoresRequestedBranch()
    {
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        Assert.Equal("BR-A", WorkflowQueueService.ResolvePartition(caller, "BR-B"));
        Assert.Equal("BR-A", WorkflowQueueService.ResolvePartition(caller, "BR-A"));
        Assert.Equal("BR-A", WorkflowQueueService.ResolvePartition(caller, null));
    }

    [Fact]
    public void ResolvePartition_Admin_UsesValidatedRequestedBranch()
    {
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");

        Assert.Equal("BR-B", WorkflowQueueService.ResolvePartition(caller, "BR-B"));
    }

    [Fact]
    public void ResolvePartition_Admin_WithoutRequestedBranch_UsesOwnBranch()
    {
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");

        Assert.Equal("BR-HQ", WorkflowQueueService.ResolvePartition(caller, null));
        Assert.Equal("BR-HQ", WorkflowQueueService.ResolvePartition(caller, "   "));
    }

    [Fact]
    public void ResolvePartition_Admin_EmptyRequestedBranch_FallsBackToOwnBranch()
    {
        var caller = new CallerContext(Guid.NewGuid(), UserRole.System, "BR-HQ");

        Assert.Equal("BR-HQ", WorkflowQueueService.ResolvePartition(caller, string.Empty));
    }

    [Fact]
    public async Task ListAsync_OnlyReturnsCallerBranchItems()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db, "BR-A", 2, now);
        await SeedAsync(db, "BR-B", 3, now);

        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var partition = WorkflowQueueService.ResolvePartition(caller, "BR-B");

        var items = await queue.ListAsync(WorkflowStage.Recommendation, partition, 50, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("BR-A", i.PartitionKey));
    }

    [Fact]
    public async Task ClaimNextAsync_OnlyClaimsCallerBranchItems()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db, "BR-A", 1, now);
        await SeedAsync(db, "BR-B", 1, now);

        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var partition = WorkflowQueueService.ResolvePartition(caller, "BR-B");

        var claimed = await queue.ClaimNextAsync(
            WorkflowStage.Recommendation,
            partition,
            caller.UserId,
            CancellationToken.None);

        Assert.Equal("BR-A", claimed.PartitionKey);
    }

    [Fact]
    public async Task ClaimNextAsync_AttackerPartitionKey_CannotClaimOtherBranch()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db, "BR-B", 1, now);

        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var partition = WorkflowQueueService.ResolvePartition(caller, "BR-B");

        await Assert.ThrowsAsync<Ebi.Alas.Api.Composition.Errors.NotFoundException>(() =>
            queue.ClaimNextAsync(WorkflowStage.Recommendation, partition, caller.UserId, CancellationToken.None));
    }
}
