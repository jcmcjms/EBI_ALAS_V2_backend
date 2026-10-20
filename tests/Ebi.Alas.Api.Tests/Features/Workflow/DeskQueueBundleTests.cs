using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

/// <summary>
/// The desk serves clients, not single applications: a bundle submitted under one
/// ApplicationGroupNo is queued, claimed and released as a single unit.
/// </summary>
public sealed class DeskQueueBundleTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"desk-bundle-{Guid.NewGuid():N}")
            .Options);

    private static (DeskQueueService Desk, WorkflowQueueService Queue, FixedTimeProvider Time) Service(
        AlasDbContext db, DateTimeOffset now)
    {
        var time = new FixedTimeProvider(now);
        var queue = new WorkflowQueueService(db, time);
        return (new DeskQueueService(db, queue, time), queue, time);
    }

    private static LoanApplication NewLoan(string lamId, string groupNo, string clientName, DateTimeOffset now) =>
        LoanApplication.Create(lamId, groupNo, clientName, "011", LoanType.New, 1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);

    private static async Task<LoanApplication> EnqueueAsync(
        AlasDbContext db,
        WorkflowQueueService queue,
        string lamId,
        string groupNo,
        string clientName,
        DateTimeOffset now,
        WorkflowStage stage = WorkflowStage.Evaluation)
    {
        var loan = NewLoan(lamId, groupNo, clientName, now);
        loan.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        await queue.EnqueueAsync(loan.Id, stage, "011", CancellationToken.None);
        return loan;
    }

    [Fact]
    public async Task GetMyDesk_GroupsBundleApplicationsUnderOneClient()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        await EnqueueAsync(db, queue, "LAM-A2", "GRP-A", "ACME CORP", now);
        await EnqueueAsync(db, queue, "LAM-B1", "GRP-B", "BETA INC", now);

        var result = await desk.GetMyDeskAsync(new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011"), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);

        var head = result.Items[0];
        Assert.Equal("GRP-A", head.GroupNo);
        Assert.Equal("ACME CORP", head.ClientName);
        Assert.Equal(1, head.Position);
        Assert.True(head.IsHead);
        Assert.Equal(["LAM-A1", "LAM-A2"], head.Applications.Select(a => a.LamId).ToArray());
        Assert.Equal(now, head.EnqueuedAt);

        var second = result.Items[1];
        Assert.Equal("GRP-B", second.GroupNo);
        Assert.Equal(2, second.Position);
        Assert.False(second.IsHead);
        Assert.Contains("2 clients waiting", result.ScopeDescription);
    }

    [Fact]
    public async Task ClaimHead_ClaimsEveryApplicationInTheBundle()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        var a1 = await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        var a2 = await EnqueueAsync(db, queue, "LAM-A2", "GRP-A", "ACME CORP", now);
        var evaluatorId = Guid.NewGuid();

        var claim = await desk.ClaimHeadAsync(new CallerContext(evaluatorId, UserRole.Evaluator, "011"), CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal("GRP-A", claim!.GroupNo);
        Assert.Equal(a1.Id, claim.LoanId);
        Assert.Equal("ACME CORP", claim.ClientName);
        Assert.Equal(["LAM-A1", "LAM-A2"], claim.Applications.Select(a => a.LamId).ToArray());

        var actives = await db.WorkflowQueueItems
            .Where(q => q.State == QueueItemState.Active && q.OwnerUserId == evaluatorId)
            .ToListAsync();
        Assert.Equal(2, actives.Count);

        await db.Entry(a1).ReloadAsync();
        await db.Entry(a2).ReloadAsync();
        Assert.Equal(evaluatorId, a1.AssignedToUserId);
        Assert.Equal(evaluatorId, a2.AssignedToUserId);
    }

    [Fact]
    public async Task GetMyDesk_AfterClaim_ShowsClientBundleAsCurrentClaim()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        await EnqueueAsync(db, queue, "LAM-A2", "GRP-A", "ACME CORP", now);
        var evaluator = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");

        await desk.ClaimHeadAsync(evaluator, CancellationToken.None);
        var result = await desk.GetMyDeskAsync(evaluator, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.NotNull(result.CurrentClaim);
        Assert.Equal("GRP-A", result.CurrentClaim!.GroupNo);
        Assert.Equal(2, result.CurrentClaim.Applications.Count);
        Assert.All(result.CurrentClaim.Applications, a => Assert.Equal(evaluator.UserId, a.OwnerUserId));
    }

    [Fact]
    public async Task ClaimHead_SkipsBundleHeldByAnotherOfficer()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        await EnqueueAsync(db, queue, "LAM-B1", "GRP-B", "BETA INC", now);

        var other = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        var mine = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        Assert.NotNull(await desk.ClaimHeadAsync(other, CancellationToken.None));

        var claim = await desk.ClaimHeadAsync(mine, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal("GRP-B", claim!.GroupNo);
        Assert.Equal("LAM-B1", claim.LamId);
    }

    [Fact]
    public async Task ClaimById_OnBundleSibling_ClaimsWholeBundle()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        var a2 = await EnqueueAsync(db, queue, "LAM-A2", "GRP-A", "ACME CORP", now);
        var evaluatorId = Guid.NewGuid();

        var claim = await desk.ClaimByIdAsync(a2.Id, new CallerContext(evaluatorId, UserRole.Evaluator, "011"), CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal("GRP-A", claim!.GroupNo);
        Assert.Equal(2, claim.Applications.Count);
        var actives = await db.WorkflowQueueItems
            .CountAsync(q => q.State == QueueItemState.Active && q.OwnerUserId == evaluatorId);
        Assert.Equal(2, actives);
    }

    [Fact]
    public async Task ClaimById_BehindHeadClient_ThrowsConflict()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        var b1 = await EnqueueAsync(db, queue, "LAM-B1", "GRP-B", "BETA INC", now);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            desk.ClaimByIdAsync(b1.Id, new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011"), CancellationToken.None));
        Assert.Contains("behind", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Release_FreesEveryApplicationInTheBundle()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        await EnqueueAsync(db, queue, "LAM-A2", "GRP-A", "ACME CORP", now);
        var evaluator = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");

        Assert.NotNull(await desk.ClaimHeadAsync(evaluator, CancellationToken.None));
        Assert.True(await desk.ReleaseClaimAsync(evaluator, CancellationToken.None));

        var actives = await db.WorkflowQueueItems.CountAsync(q => q.State == QueueItemState.Active);
        Assert.Equal(0, actives);

        var result = await desk.GetMyDeskAsync(evaluator, CancellationToken.None);
        Assert.Null(result.CurrentClaim);
        var client = Assert.Single(result.Items);
        Assert.Equal(2, client.Applications.Count);
    }

    [Fact]
    public async Task ClaimHead_WhileHoldingBundle_AbsorbsLateSibling()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        var a1 = await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        var evaluator = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        Assert.NotNull(await desk.ClaimHeadAsync(evaluator, CancellationToken.None));

        // Sibling returns from revision while the bundle is held.
        var a2 = NewLoan("LAM-A2", "GRP-A", "ACME CORP", now);
        a2.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(a2);
        await db.SaveChangesAsync();
        await queue.EnqueueAsync(a2.Id, WorkflowStage.Evaluation, "011", CancellationToken.None);

        var claim = await desk.ClaimHeadAsync(evaluator, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal(a1.Id, claim!.LoanId);
        Assert.Equal(["LAM-A1", "LAM-A2"], claim.Applications.Select(a => a.LamId).ToArray());
        var actives = await db.WorkflowQueueItems
            .CountAsync(q => q.State == QueueItemState.Active && q.OwnerUserId == evaluator.UserId);
        Assert.Equal(2, actives);
    }

    [Fact]
    public async Task ClaimById_OnOwnClaimedLoan_ResumesBundle()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        var a1 = await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        await EnqueueAsync(db, queue, "LAM-A2", "GRP-A", "ACME CORP", now);
        var evaluator = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");

        Assert.NotNull(await desk.ClaimHeadAsync(evaluator, CancellationToken.None));
        var resumed = await desk.ClaimByIdAsync(a1.Id, evaluator, CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.Equal("GRP-A", resumed!.GroupNo);
        Assert.Equal(2, resumed.Applications.Count);
    }

    [Fact]
    public async Task ClaimById_OnLoanHeldByAnotherOfficer_ThrowsConflict()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var (desk, queue, _) = Service(db, now);

        var a1 = await EnqueueAsync(db, queue, "LAM-A1", "GRP-A", "ACME CORP", now);
        var other = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        Assert.NotNull(await desk.ClaimHeadAsync(other, CancellationToken.None));

        await Assert.ThrowsAsync<ConflictException>(() =>
            desk.ClaimByIdAsync(a1.Id, new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011"), CancellationToken.None));
    }
}
