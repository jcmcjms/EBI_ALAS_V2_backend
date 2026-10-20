using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanGroupStatusHandlerTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"group-status-{Guid.NewGuid():N}")
            .Options);

    private static LoanWorkflowService Service(AlasDbContext db) => new(
        db,
        new WorkflowQueueService(db, TimeProvider.System),
        null,
        new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
        TimeProvider.System);

    private static LoanApplication BundleLoan(string lamId, string groupNo, LoanStatus status, string branch = "011")
    {
        var loan = LoanApplication.Create(
            lamId, groupNo, "Bundle Client", branch, LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, Guid.NewGuid(), DateTimeOffset.UtcNow, "A16", "A16");
        if (status != LoanStatus.ForRecommendation)
        {
            loan.TransitionTo(status, DateTimeOffset.UtcNow);
        }

        return loan;
    }

    [Fact]
    public async Task Apply_ForApproval_TransitionsEveryEligibleLoanWithComments()
    {
        await using var db = Db();
        var a1 = BundleLoan("LAM-1", "GRP-B", LoanStatus.ForChecking);
        var a2 = BundleLoan("LAM-2", "GRP-B", LoanStatus.ForChecking);
        db.LoanApplications.AddRange(a1, a2);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        var result = await LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-B",
            new GroupStatusRequest("ForApproval", "  Bundle capacity verified.  ", null),
            CancellationToken.None);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);

        await db.Entry(a1).ReloadAsync();
        await db.Entry(a2).ReloadAsync();
        Assert.Equal(LoanStatus.ForApproval, a1.Status);
        Assert.Equal(LoanStatus.ForApproval, a2.Status);

        var comments = await db.LoanApplicationActions
            .Where(a => a.LoanApplicationId == a1.Id)
            .Select(a => a.Comments)
            .SingleAsync();
        Assert.Equal("Bundle capacity verified.", comments);
    }

    [Fact]
    public async Task Apply_MixedStatuses_FailsIneligibleLoansWithoutAbortingBatch()
    {
        await using var db = Db();
        var ready = BundleLoan("LAM-1", "GRP-M", LoanStatus.ForChecking);
        var notReady = BundleLoan("LAM-2", "GRP-M", LoanStatus.ForRecommendation);
        db.LoanApplications.AddRange(ready, notReady);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        var result = await LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-M",
            new GroupStatusRequest("ForApproval", null, null),
            CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        var failed = result.Results.Single(r => !r.Succeeded);
        Assert.Equal("LAM-2", failed.LamId);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));

        await db.Entry(notReady).ReloadAsync();
        Assert.Equal(LoanStatus.ForRecommendation, notReady.Status);
    }

    [Fact]
    public async Task Apply_LimitsToSelectedLoanIds()
    {
        await using var db = Db();
        var a1 = BundleLoan("LAM-1", "GRP-S", LoanStatus.ForChecking);
        var a2 = BundleLoan("LAM-2", "GRP-S", LoanStatus.ForChecking);
        db.LoanApplications.AddRange(a1, a2);
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        var result = await LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-S",
            new GroupStatusRequest("ForApproval", null, [a2.Id]),
            CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(a2.Id, result.Results.Single().LoanId);
        await db.Entry(a1).ReloadAsync();
        Assert.Equal(LoanStatus.ForChecking, a1.Status);
    }

    [Fact]
    public async Task Apply_ForRevision_WithoutComments_ThrowsValidation()
    {
        await using var db = Db();
        db.LoanApplications.Add(BundleLoan("LAM-1", "GRP-R", LoanStatus.ForChecking));
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        await Assert.ThrowsAsync<ValidationException>(() => LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-R",
            new GroupStatusRequest("ForRevision", "  ", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Apply_UnknownStatus_ThrowsValidation()
    {
        await using var db = Db();
        db.LoanApplications.Add(BundleLoan("LAM-1", "GRP-U", LoanStatus.ForChecking));
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        await Assert.ThrowsAsync<ValidationException>(() => LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-U",
            new GroupStatusRequest("Disbursed", null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Apply_UnknownGroup_ThrowsNotFound()
    {
        await using var db = Db();
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "011");
        await Assert.ThrowsAsync<NotFoundException>(() => LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-MISSING",
            new GroupStatusRequest("ForApproval", null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Apply_OtherBranchCaller_CannotTouchBundle()
    {
        await using var db = Db();
        db.LoanApplications.Add(BundleLoan("LAM-1", "GRP-X", LoanStatus.ForChecking, branch: "011"));
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "999");
        await Assert.ThrowsAsync<NotFoundException>(() => LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-X",
            new GroupStatusRequest("ForApproval", null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Apply_WrongRole_ReportsPerLoanForbidden()
    {
        await using var db = Db();
        db.LoanApplications.Add(BundleLoan("LAM-1", "GRP-ROLE", LoanStatus.ForChecking));
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "011");
        var result = await LoanGroupStatusHandler.ApplyAsync(
            db, Service(db), caller, "GRP-ROLE",
            new GroupStatusRequest("ForApproval", null, null),
            CancellationToken.None);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
    }
}
