using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

public sealed class DeskQueueServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"desk-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static DeskQueueService Service(AlasDbContext db, DateTimeOffset now) =>
        new(db, new WorkflowQueueService(db, new FixedTimeProvider(now)), new FixedTimeProvider(now));

    private static LoanApplication NewLoan(string lamId, DateTimeOffset now) =>
        NewLoan(lamId, Guid.NewGuid(), now);

    private static LoanApplication NewLoan(string lamId, DateTimeOffset now, string groupNo) =>
        NewLoan(lamId, Guid.NewGuid(), now, groupNo);

    private static LoanApplication NewLoan(string lamId, Guid createdBy, DateTimeOffset now, string groupNo = "GRP-1") =>
        LoanApplication.Create(lamId, groupNo, "Client", "011", LoanType.New, 1000m, 30, 0.05m, 50m, 10m, 940m, createdBy, now);

    [Fact]
    public async Task GetMyDeskAsync_Recommender_SeesQueuedRecommendationItems()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var loan = NewLoan("LAM-1", now);
        db.LoanApplications.Add(loan);
        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        await queue.EnqueueAsync(loan.Id, WorkflowStage.Recommendation, "011", CancellationToken.None);

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "011");
        var desk = await Service(db, now).GetMyDeskAsync(caller, CancellationToken.None);

        Assert.Equal("Recommendation Desk", desk.DeskLabel);
        var client = Assert.Single(desk.Items);
        Assert.Equal("GRP-1", client.GroupNo);
        var item = Assert.Single(client.Applications);
        Assert.Equal("LAM-1", item.LamId);
        Assert.True(client.IsHead);
        Assert.Null(desk.CurrentClaim);
    }

    [Fact]
    public async Task ClaimHeadAsync_ThenGetMyDesk_ReturnsCurrentClaim()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var loan = NewLoan("LAM-2", now);
        db.LoanApplications.Add(loan);
        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        await queue.EnqueueAsync(loan.Id, WorkflowStage.Recommendation, "011", CancellationToken.None);

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "011");
        var service = Service(db, now);
        var claim = await service.ClaimHeadAsync(caller, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal("LAM-2", claim!.LamId);

        var desk = await service.GetMyDeskAsync(caller, CancellationToken.None);
        Assert.NotNull(desk.CurrentClaim);
        Assert.Empty(desk.Items);
    }

    [Fact]
    public async Task TryMapStage_MapsWorkflowRoles()
    {
        Assert.True(DeskQueueService.TryMapStage(UserRole.Recommender, out var rec));
        Assert.Equal(WorkflowStage.Recommendation, rec);
        Assert.True(DeskQueueService.TryMapStage(UserRole.Evaluator, out var ev));
        Assert.Equal(WorkflowStage.Evaluation, ev);
        Assert.True(DeskQueueService.TryMapStage(UserRole.Approver, out var ap));
        Assert.Equal(WorkflowStage.Approval, ap);
        Assert.False(DeskQueueService.TryMapStage(UserRole.Admin, out _));
        Assert.False(DeskQueueService.TryMapStage(UserRole.Encoder, out _));
    }

    [Fact]
    public async Task Cancel_WhileEvaluatorHoldsClaim_ReleasesDeskAndAllowsNextClaim()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var time = new FixedTimeProvider(now);
        var queue = new WorkflowQueueService(db, time);
        var desk = new DeskQueueService(db, queue, time);

        var creator = Guid.NewGuid();
        var loan = NewLoan("LAM-CANCEL", creator, now);
        loan.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(loan);
        await queue.EnqueueAsync(loan.Id, WorkflowStage.Evaluation, "011", CancellationToken.None);

        var evaluatorId = Guid.NewGuid();
        var evaluator = new CallerContext(evaluatorId, UserRole.Evaluator, "011");
        var claimed = await desk.ClaimHeadAsync(evaluator, CancellationToken.None);
        Assert.NotNull(claimed);

        var next = NewLoan("LAM-NEXT", now, groupNo: "GRP-NEXT");
        next.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(next);
        await queue.EnqueueAsync(next.Id, WorkflowStage.Evaluation, "011", CancellationToken.None);

        var encoder = new CallerContext(creator, UserRole.Encoder, "011");
        await new Ebi.Alas.Api.Features.LoanApplications.WorkflowActions.LoanWorkflowService(
            db,
            queue,
            notifier: null,
            new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, time),
            time)
            .CancelAsync(loan.Id, encoder, CancellationToken.None);

        var deskAfter = await desk.GetMyDeskAsync(evaluator, CancellationToken.None);
        Assert.Null(deskAfter.CurrentClaim);
        Assert.DoesNotContain(deskAfter.Items, c => c.Applications.Any(a => a.LamId == "LAM-CANCEL"));

        var claimAfter = await desk.ClaimHeadAsync(evaluator, CancellationToken.None);
        Assert.NotNull(claimAfter);
        Assert.Equal("LAM-NEXT", claimAfter!.LamId);

        var remaining = await db.WorkflowQueueItems
            .Where(q => q.LoanApplicationId == loan.Id && q.State != QueueItemState.Completed)
            .ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task GetMyDeskAsync_WhenClaimedLoanAlreadyCancelled_DropsStaleClaim()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var time = new FixedTimeProvider(now);
        var queue = new WorkflowQueueService(db, time);
        var desk = new DeskQueueService(db, queue, time);

        var loan = NewLoan("LAM-STALE", now);
        loan.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(loan);
        await queue.EnqueueAsync(loan.Id, WorkflowStage.Evaluation, "011", CancellationToken.None);

        var evaluator = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        Assert.NotNull(await desk.ClaimHeadAsync(evaluator, CancellationToken.None));

        loan.Cancel(now);
        await db.SaveChangesAsync();

        var deskAfter = await desk.GetMyDeskAsync(evaluator, CancellationToken.None);
        Assert.Null(deskAfter.CurrentClaim);

        var claimAfter = await desk.ClaimHeadAsync(evaluator, CancellationToken.None);
        Assert.Null(claimAfter);
    }

    private static LoanApplication NewLoanOnBranch(string lamId, string branchId, string groupNo, DateTimeOffset now) =>
        LoanApplication.Create(lamId, groupNo, "Client", branchId, LoanType.New, 1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);

    [Fact]
    public async Task Evaluator_Desk_ShowsAndClaimsOtherBranchPartition()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        db.Branches.Add(Ebi.Alas.Api.Features.Branches.Branch.Create("001", "Main", "A1", now));
        db.Branches.Add(Ebi.Alas.Api.Features.Branches.Branch.Create("002", "Satellite", "A1", now));
        await db.SaveChangesAsync();

        var loan = NewLoanOnBranch("LAM-REMOTE", "002", "GRP-R", now);
        loan.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(loan);
        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        await queue.EnqueueAsync(loan.Id, WorkflowStage.Evaluation, "002", CancellationToken.None);

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "001");
        var service = Service(db, now);

        var desk = await service.GetMyDeskAsync(caller, CancellationToken.None);
        var client = Assert.Single(desk.Items);
        Assert.Equal("LAM-REMOTE", Assert.Single(client.Applications).LamId);
        Assert.Contains("All branches", desk.ScopeDescription);

        var claim = await service.ClaimHeadAsync(caller, CancellationToken.None);
        Assert.NotNull(claim);
        Assert.Equal("LAM-REMOTE", claim!.LamId);
    }

    [Fact]
    public async Task Recommender_Desk_RemainsHomePartition()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        db.Branches.Add(Ebi.Alas.Api.Features.Branches.Branch.Create("001", "Main", "A1", now));
        db.Branches.Add(Ebi.Alas.Api.Features.Branches.Branch.Create("002", "Satellite", "A1", now));
        await db.SaveChangesAsync();

        var loan = NewLoanOnBranch("LAM-REMOTE-REC", "002", "GRP-RR", now);
        loan.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(loan);
        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));
        await queue.EnqueueAsync(loan.Id, WorkflowStage.Recommendation, "002", CancellationToken.None);

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "001");
        var service = Service(db, now);

        var desk = await service.GetMyDeskAsync(caller, CancellationToken.None);
        Assert.Empty(desk.Items);

        var claim = await service.ClaimHeadAsync(caller, CancellationToken.None);
        Assert.Null(claim);
    }
}
