using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Deviations;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanApprovalAuthorityTests
{
    private static AlasDbContext Db() =>
        new(new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid():N}")
            .Options);

    private static LoanWorkflowService Service(AlasDbContext db) =>
        new(
            db,
            new WorkflowQueueService(db, TimeProvider.System),
            notifier: null,
            new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
            TimeProvider.System);

    private static CallerContext Caller(Guid id, UserRole role, string branch) =>
        new(id, role, branch);

    private static async Task<(LoanApplication Loan, User Evaluator, User BranchHead)> SeedAsync(
        AlasDbContext db,
        LoanType type = LoanType.Reloan,
        decimal principal = 100_000m,
        int? majorDeviation = null)
    {
        var evaluator = User.Create(
            "eval", "h", "E", "Val", "", "e@x.com", "011",
            UserRole.Evaluator, DateTimeOffset.UtcNow, "Credit Checker");
        var head = User.Create(
            "head", "h", "B", "Head", "", "h@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Branch Head");
        db.Users.AddRange(evaluator, head);

        var loan = LoanApplication.Create(
            "LAM-9", "G-9", "Client", "011", type,
            principal, 30, 0.05m, 10m, 50m, principal - 60m,
            evaluator.Id, DateTimeOffset.UtcNow, "A16", "A16");
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);

        if (majorDeviation is { } severity)
        {
            db.LoanDeviations.Add(LoanDeviation.Create(
                loan.Id,
                "AGE",
                "Age not within the prescribed parameters",
                severity,
                DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync();
        return (loan, evaluator, head);
    }

    [Fact]
    public async Task Evaluate_AssignsBranchHead_ForSmallReloan()
    {
        await using var db = Db();
        var (loan, evaluator, head) = await SeedAsync(db);

        await Service(db).EvaluateAsync(
            loan.Id,
            recommend: true,
            Caller(evaluator.Id, UserRole.Evaluator, "011"),
            CancellationToken.None,
            "Capacity verified for approval.");

        var reloaded = await db.LoanApplications.AsNoTracking().FirstAsync(l => l.Id == loan.Id);
        Assert.Equal(head.Id, reloaded.AssignedToUserId);
    }

    [Fact]
    public async Task Approve_NonAssignee_IsForbidden()
    {
        await using var db = Db();
        var (loan, evaluator, head) = await SeedAsync(db);
        await Service(db).EvaluateAsync(
            loan.Id,
            true,
            Caller(evaluator.Id, UserRole.Evaluator, "011"),
            CancellationToken.None,
            "Capacity verified for approval.");

        var other = User.Create(
            "area", "h", "A", "Area", "", "a@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Area Head");
        db.Users.Add(other);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db).ApproveAsync(
            loan.Id,
            Caller(other.Id, UserRole.Approver, "011"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Approve_AssigneeWithSufficientLevel_Succeeds()
    {
        await using var db = Db();
        var (loan, evaluator, head) = await SeedAsync(db);
        await Service(db).EvaluateAsync(
            loan.Id,
            true,
            Caller(evaluator.Id, UserRole.Evaluator, "011"),
            CancellationToken.None,
            "Capacity verified for approval.");

        var result = await Service(db).ApproveAsync(
            loan.Id,
            Caller(head.Id, UserRole.Approver, "011"),
            CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.Approved), result.Status);
    }

    [Fact]
    public async Task Approve_SharedPool_FirstToServe_Succeeds()
    {
        await using var db = Db();
        var evaluator = User.Create(
            "eval", "h", "E", "Val", "", "e@x.com", "011",
            UserRole.Evaluator, DateTimeOffset.UtcNow, "Credit Checker");
        var product = User.Create(
            "prod", "h", "P", "Head", "", "p@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Product Head");
        var credit = User.Create(
            "cred", "h", "C", "Head", "", "c@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Credit Head");
        db.Users.AddRange(evaluator, product, credit);

        var loan = LoanApplication.Create(
            "LAM-POOL", "G-POOL", "Client", "011", LoanType.New,
            500_000m, 30, 0.05m, 10m, 50m, 499_000m,
            evaluator.Id, DateTimeOffset.UtcNow, "A16", "A16");
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        await Service(db).EvaluateAsync(
            loan.Id,
            true,
            Caller(evaluator.Id, UserRole.Evaluator, "011"),
            CancellationToken.None,
            "Capacity verified for approval.");

        var afterEval = await db.LoanApplications.AsNoTracking().FirstAsync(l => l.Id == loan.Id);
        Assert.Null(afterEval.AssignedToUserId);

        var result = await Service(db).ApproveAsync(
            loan.Id,
            Caller(product.Id, UserRole.Approver, "011"),
            CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.Approved), result.Status);
    }

    [Fact]
    public async Task Approve_InsufficientLevel_IsForbidden()
    {
        await using var db = Db();
        var (loan, evaluator, head) = await SeedAsync(db, majorDeviation: 2);
        var president = User.Create(
            "ceo", "h", "C", "EO", "", "c@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "President & CEO");
        db.Users.Add(president);
        await db.SaveChangesAsync();

        await Service(db).EvaluateAsync(
            loan.Id,
            true,
            Caller(evaluator.Id, UserRole.Evaluator, "011"),
            CancellationToken.None,
            "Capacity verified for approval.");

        // Branch Head is assigned only if president is unavailable for scope; major needs President.
        // Regardless of who is assigned, Branch Head must not approve a major loan.
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db).ApproveAsync(
            loan.Id,
            Caller(head.Id, UserRole.Approver, "011"),
            CancellationToken.None));
    }
}
