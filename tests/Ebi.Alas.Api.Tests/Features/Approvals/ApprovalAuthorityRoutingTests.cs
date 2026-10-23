using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApprovalAuthorityRoutingTests
{
    private static AlasDbContext Db() =>
        new(new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"routing-{Guid.NewGuid():N}")
            .Options);

    private static LoanWorkflowService Service(AlasDbContext db) =>
        new(
            db,
            new WorkflowQueueService(db, TimeProvider.System),
            notifier: null,
            new ApplicationArrivalNotifier(db, null, TimeProvider.System),
            TimeProvider.System);

    private static CallerContext Caller(Guid id, UserRole role, string branch) =>
        new(id, role, branch);

    [Fact]
    public async Task TryResolveAsync_EmptyCatalog_ReturnsNull()
    {
        await using var db = Db();
        var loan = LoanApplication.Create(
            "LAM-1", "G-1", "Client", "011", LoanType.Reloan,
            100_000m, 30, 0.05m, 10m, 50m, 99_000m,
            Guid.NewGuid(), DateTimeOffset.UtcNow, "A16", "A16");

        var result = await new ApprovalRoutingService(db).TryResolveAsync(loan, 0, false, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_CatalogCoversLoan_ReturnsAuthorityAndCandidate()
    {
        await using var db = Db();
        var approver = User.Create(
            "head", "h", "B", "Head", "", "h@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Branch Head");
        db.Users.Add(approver);

        var authority = ApprovalAuthority.Create("Branch Head", 1, 0, true, true, 0, 1_000_000m, 0);
        db.ApprovalAuthorities.Add(authority);
        db.ApprovalAuthorityUsers.Add(new ApprovalAuthorityUser
        {
            ApprovalAuthorityId = authority.Id,
            UserId = approver.Id,
        });
        await db.SaveChangesAsync();

        var loan = LoanApplication.Create(
            "LAM-1", "G-1", "Client", "011", LoanType.Reloan,
            100_000m, 30, 0.05m, 10m, 50m, 99_000m,
            Guid.NewGuid(), DateTimeOffset.UtcNow, "A16", "A16");

        var result = await new ApprovalRoutingService(db).TryResolveAsync(loan, 0, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.RequiredTier);
        Assert.Equal("Branch Head", result.MatchedRule);
        Assert.Null(result.NoAuthorityReason);
        Assert.Equal([approver.Id], result.Candidates.Select(c => c.UserId));
    }

    [Fact]
    public async Task Evaluate_AssignsCatalogApprover_WhenConfigured()
    {
        await using var db = Db();
        var evaluator = User.Create(
            "eval", "h", "E", "Val", "", "e@x.com", "011",
            UserRole.Evaluator, DateTimeOffset.UtcNow, "Credit Checker");
        var head = User.Create(
            "head", "h", "B", "Head", "", "h@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Branch Head");
        db.Users.AddRange(evaluator, head);

        var authority = ApprovalAuthority.Create("Branch Head", 1, 0, true, true, 0, 1_000_000m, 0);
        db.ApprovalAuthorities.Add(authority);
        db.ApprovalAuthorityUsers.Add(new ApprovalAuthorityUser
        {
            ApprovalAuthorityId = authority.Id,
            UserId = head.Id,
        });

        var loan = LoanApplication.Create(
            "LAM-9", "G-9", "Client", "011", LoanType.Reloan,
            100_000m, 30, 0.05m, 10m, 50m, 99_000m,
            evaluator.Id, DateTimeOffset.UtcNow, "A16", "A16");
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        await Service(db).EvaluateAsync(
            loan.Id,
            recommend: true,
            Caller(evaluator.Id, UserRole.Evaluator, "011"),
            CancellationToken.None,
            "Capacity verified for approval.");

        var reloaded = await db.LoanApplications.AsNoTracking().FirstAsync(l => l.Id == loan.Id);
        Assert.Equal(head.Id, reloaded.AssignedToUserId);
    }
}
