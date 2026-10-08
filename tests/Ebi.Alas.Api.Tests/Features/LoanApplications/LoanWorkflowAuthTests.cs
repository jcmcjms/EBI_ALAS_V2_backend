using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanWorkflowAuthTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-wf-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication Loan(string branchId, Guid createdBy)
    {
        var now = DateTimeOffset.UtcNow;
        return LoanApplication.Create(
            $"LAM-{Guid.NewGuid():N}"[..32],
            "G-1",
            "Client",
            branchId,
            LoanType.New,
            1000m,
            30,
            0.05m,
            50m,
            10m,
            940m,
            createdBy,
            now);
    }

    private static LoanWorkflowService Service(AlasDbContext db) =>
        new(db, new WorkflowQueueService(db, TimeProvider.System), notifier: null, TimeProvider.System);

    [Fact]
    public async Task Approve_WithEncoderRole_ThrowsForbidden()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(db).ApproveAsync(loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task Approve_WithApproverRole_Succeeds()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        loan.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Approver, "BR-A");
        var result = await Service(db).ApproveAsync(loan.Id, caller, CancellationToken.None);
        Assert.Equal(nameof(LoanStatus.Approved), result.Status);
    }

    [Fact]
    public async Task Recommend_WithEvaluatorRole_ThrowsForbidden()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "BR-A");
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(db).RecommendAsync(loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task Recommend_WithRecommenderRole_Succeeds()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "BR-A");
        var result = await Service(db).RecommendAsync(loan.Id, caller, CancellationToken.None);
        Assert.Equal(nameof(LoanStatus.ForChecking), result.Status);
    }

    [Fact]
    public async Task Approve_OtherBranch_ThrowsNotFound()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Approver, "BR-B");
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(db).ApproveAsync(loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task Evaluate_WithRecommenderRole_ThrowsForbidden()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "BR-A");
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(db).EvaluateAsync(loan.Id, recommend: true, caller, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_CreatorWithEncoderRole_Succeeds()
    {
        await using var db = Db();
        var creator = Guid.NewGuid();
        var loan = Loan("BR-A", creator);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(creator, UserRole.Encoder, "BR-A");
        var result = await Service(db).CancelAsync(loan.Id, caller, CancellationToken.None);
        Assert.Equal(nameof(LoanStatus.Cancelled), result.Status);
    }

    [Fact]
    public async Task Cancel_NonCreatorEncoder_ThrowsForbidden()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(db).CancelAsync(loan.Id, caller, CancellationToken.None));
    }
}
