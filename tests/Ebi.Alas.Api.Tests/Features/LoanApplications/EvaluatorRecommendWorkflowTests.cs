using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class EvaluatorRecommendWorkflowTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"eval-rec-{Guid.NewGuid():N}")
            .Options);

    private static LoanApplication LoanInChecking(Guid createdBy) =>
        LoanApplication.Create(
            "LAM-1", "G-1", "Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, createdBy, DateTimeOffset.UtcNow, "A16", "A16");

    private static Ebi.Alas.Api.Features.LoanApplications.WorkflowActions.LoanWorkflowService Service(AlasDbContext db) =>
        new(
            db,
            new WorkflowQueueService(db, TimeProvider.System),
            null,
            new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
            TimeProvider.System);

    [Fact]
    public async Task Evaluate_Recommend_ForChecking_IsAllowedForEvaluator()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await Service(db).EvaluateAsync(
            loan.Id, true, new CallerContext(userId, UserRole.Evaluator, "001"), CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForApproval), result.Status);
    }

    [Fact]
    public async Task Recommend_ForChecking_IsAllowedForEvaluator()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await Service(db).RecommendAsync(
            loan.Id, new CallerContext(userId, UserRole.Evaluator, "001"), CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForApproval), result.Status);
    }

    [Fact]
    public async Task Recommend_ForRecommendation_IsStillForRecommender()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await Service(db).RecommendAsync(
            loan.Id, new CallerContext(userId, UserRole.Recommender, "001"), CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForChecking), result.Status);
    }
}
