using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Dashboard;

public sealed class DashboardServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"dash-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication NewLoan(string lamId, string branchId, DateTimeOffset now) =>
        LoanApplication.Create(
            lamId,
            "GRP-1",
            "Client",
            branchId,
            LoanType.New,
            1000m,
            30,
            0.05m,
            50m,
            10m,
            940m,
            Guid.NewGuid(),
            now);

    [Fact]
    public async Task GetOverviewAsync_CountsEachBucket()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new DashboardService(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "011");

        db.LoanApplications.Add(NewLoan("LAM-REC", "011", now));

        var checking = NewLoan("LAM-CHK", "011", now);
        checking.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(checking);

        var approval = NewLoan("LAM-APR", "011", now);
        approval.TransitionTo(LoanStatus.ForChecking, now);
        approval.TransitionTo(LoanStatus.ForApproval, now);
        db.LoanApplications.Add(approval);

        var approved = NewLoan("LAM-OK", "011", now);
        approved.TransitionTo(LoanStatus.ForChecking, now);
        approved.TransitionTo(LoanStatus.ForApproval, now);
        approved.TransitionTo(LoanStatus.Approved, now);
        db.LoanApplications.Add(approved);

        var revision = NewLoan("LAM-REV", "011", now);
        revision.TransitionTo(LoanStatus.ForRevision, now);
        db.LoanApplications.Add(revision);

        await db.SaveChangesAsync();

        var overview = await service.GetOverviewAsync(caller, new DateOnly(2026, 10, 9), CancellationToken.None);

        Assert.Equal(1, overview.PendingRecommendation);
        Assert.Equal(1, overview.PendingEvaluation);
        Assert.Equal(1, overview.PendingApproval);
        Assert.Equal(1, overview.ApprovedToday);
        Assert.Equal(1, overview.Pushbacks);
    }

    [Fact]
    public async Task GetOverviewAsync_NonAdmin_ScopedToCallerBranch()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new DashboardService(db);

        db.LoanApplications.Add(NewLoan("LAM-01-REC", "BR-01", now));
        db.LoanApplications.Add(NewLoan("LAM-02-REC", "BR-02", now));

        var br02Approved = NewLoan("LAM-02-OK", "BR-02", now);
        br02Approved.TransitionTo(LoanStatus.ForChecking, now);
        br02Approved.TransitionTo(LoanStatus.ForApproval, now);
        br02Approved.TransitionTo(LoanStatus.Approved, now);
        db.LoanApplications.Add(br02Approved);

        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-01");
        var overview = await service.GetOverviewAsync(caller, new DateOnly(2026, 10, 9), CancellationToken.None);

        Assert.Equal(1, overview.PendingRecommendation);
        Assert.Equal(0, overview.PendingEvaluation);
        Assert.Equal(0, overview.PendingApproval);
        Assert.Equal(0, overview.ApprovedToday);
        Assert.Equal(0, overview.Pushbacks);
    }

    [Fact]
    public async Task GetOverviewAsync_Admin_SeesAllBranches()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new DashboardService(db);

        db.LoanApplications.Add(NewLoan("LAM-01-REC", "BR-01", now));
        db.LoanApplications.Add(NewLoan("LAM-02-REC", "BR-02", now));

        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");
        var overview = await service.GetOverviewAsync(caller, new DateOnly(2026, 10, 9), CancellationToken.None);

        Assert.Equal(2, overview.PendingRecommendation);
    }
}
