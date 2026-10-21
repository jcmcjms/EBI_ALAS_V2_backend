using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class ReviewDraftTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"review-draft-{Guid.NewGuid():N}")
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
            "LAM-1", "G-1", "Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, createdBy, DateTimeOffset.UtcNow, "A16", "A16");
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        return loan;
    }

    [Fact]
    public void Create_StoresRemarksAndTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var draft = ReviewDraft.Create(Guid.NewGuid(), Guid.NewGuid(), "  check the CIBI report  ", now);

        Assert.Equal("  check the CIBI report  ", draft.Remarks);
        Assert.Equal(now, draft.UpdatedAt);
    }

    [Fact]
    public void Create_OverMaxLength_Throws()
    {
        var remarks = new string('x', ReviewDraft.MaxRemarksLength + 1);
        Assert.Throws<ArgumentException>(() =>
            ReviewDraft.Create(Guid.NewGuid(), Guid.NewGuid(), remarks, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Update_ReplacesRemarksAndBumpsTimestamp()
    {
        var draft = ReviewDraft.Create(Guid.NewGuid(), Guid.NewGuid(), "first", DateTimeOffset.UtcNow);
        var later = DateTimeOffset.UtcNow.AddMinutes(5);

        draft.Update("second", later);

        Assert.Equal("second", draft.Remarks);
        Assert.Equal(later, draft.UpdatedAt);
    }

    [Fact]
    public async Task Evaluate_WithRemarks_RecordsThemInTheActionHistory()
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
            "  Capacity verified, no adverse findings.  ");

        var action = await db.LoanApplicationActions.SingleAsync(a => a.LoanApplicationId == loan.Id);
        Assert.Equal("Capacity verified, no adverse findings.", action.Comments);
    }

    private static async Task<User> SeedProductHead(AlasDbContext db, Guid userId)
    {
        var user = User.Create(
            $"ph-{userId:N}", "hash", "P", "Head", "", "ph@x.com", "001",
            UserRole.Approver, DateTimeOffset.UtcNow, "Product Head");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Approve_WithoutRemarks_KeepsHistoryCommentNull()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        loan.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        var head = await SeedProductHead(db, userId);
        await db.SaveChangesAsync();

        await Service(db).ApproveAsync(
            loan.Id,
            new CallerContext(head.Id, UserRole.Approver, "001"),
            CancellationToken.None);

        var action = await db.LoanApplicationActions.SingleAsync(a => a.LoanApplicationId == loan.Id);
        Assert.Null(action.Comments);
    }

    [Fact]
    public async Task Approve_WithOverlongRemarks_ThrowsValidation()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        loan.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        var head = await SeedProductHead(db, userId);
        await db.SaveChangesAsync();

        var remarks = new string('x', ReviewDraft.MaxRemarksLength + 1);
        await Assert.ThrowsAsync<ValidationException>(() => Service(db).ApproveAsync(
            loan.Id,
            new CallerContext(head.Id, UserRole.Approver, "001"),
            CancellationToken.None,
            remarks));
    }

    [Fact]
    public async Task WorkflowAction_ClearsEveryReviewerDraftForTheLoan()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanInChecking(userId);
        db.LoanApplications.Add(loan);

        var evaluatorDraft = ReviewDraft.Create(loan.Id, userId, "auto-saved evaluator notes", DateTimeOffset.UtcNow);
        var approverDraft = ReviewDraft.Create(loan.Id, Guid.NewGuid(), "auto-saved approver notes", DateTimeOffset.UtcNow);
        var otherLoanDraft = ReviewDraft.Create(Guid.NewGuid(), userId, "unrelated", DateTimeOffset.UtcNow);
        db.ReviewDrafts.AddRange(evaluatorDraft, approverDraft, otherLoanDraft);
        await db.SaveChangesAsync();

        await Service(db).EvaluateAsync(
            loan.Id,
            recommend: true,
            new CallerContext(userId, UserRole.Evaluator, "001"),
            CancellationToken.None,
            "final remarks");

        var remaining = await db.ReviewDrafts.Select(d => d.Id).ToListAsync();
        Assert.Equal([otherLoanDraft.Id], remaining);
    }
}
