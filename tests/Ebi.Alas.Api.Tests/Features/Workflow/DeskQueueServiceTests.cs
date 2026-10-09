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
        LoanApplication.Create(lamId, "GRP-1", "Client", "011", LoanType.New, 1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);

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
        var item = Assert.Single(desk.Items);
        Assert.Equal("LAM-1", item.LamId);
        Assert.True(item.IsHead);
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
}
