using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class EvaluateRecommendRemarksTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"eval-remarks-{Guid.NewGuid():N}")
            .Options);

    private static LoanWorkflowService Service(AlasDbContext db) => new(
        db,
        new WorkflowQueueService(db, TimeProvider.System),
        null,
        new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
        TimeProvider.System);

    private static LoanApplication LoanInChecking(Guid createdBy)
    {
        var loan = LoanApplication.Create(
            "LAM-ER", "G-ER", "Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, createdBy, DateTimeOffset.UtcNow, "A16", "A16");
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        return loan;
    }

    [Fact]
    public async Task Evaluate_Recommend_WithoutRemarks_ThrowsValidation()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => Service(db).EvaluateAsync(
            loan.Id,
            recommend: true,
            new CallerContext(userId, UserRole.Evaluator, "001"),
            CancellationToken.None,
            remarks: null));
    }

    [Fact]
    public async Task Evaluate_Recommend_ShortRemarks_ThrowsValidation()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => Service(db).EvaluateAsync(
            loan.Id,
            recommend: true,
            new CallerContext(userId, UserRole.Evaluator, "001"),
            CancellationToken.None,
            remarks: "short"));
    }

    [Fact]
    public async Task Evaluate_Recommend_WithRemarks_SucceedsAndStoresComment()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        await Service(db).EvaluateAsync(
            loan.Id,
            recommend: true,
            new CallerContext(userId, UserRole.Evaluator, "001"),
            CancellationToken.None,
            remarks: "Capacity verified for approval.");

        var action = await db.LoanApplicationActions.SingleAsync(a => a.LoanApplicationId == loan.Id);
        Assert.Equal("EvaluatedRecommended", action.Action);
        Assert.Equal("Capacity verified for approval.", action.Comments);
    }
}
