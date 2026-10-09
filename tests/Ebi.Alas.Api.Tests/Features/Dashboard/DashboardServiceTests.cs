using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
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

    private static LoanApplication NewLoan(string lamId, DateTimeOffset now) =>
        LoanApplication.Create(
            lamId,
            "GRP-1",
            "Client",
            "011",
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
        var service = new DashboardService(db, new FixedTimeProvider(now));

        db.LoanApplications.Add(NewLoan("LAM-REC", now));

        var checking = NewLoan("LAM-CHK", now);
        checking.TransitionTo(LoanStatus.ForChecking, now);
        db.LoanApplications.Add(checking);

        var approval = NewLoan("LAM-APR", now);
        approval.TransitionTo(LoanStatus.ForChecking, now);
        approval.TransitionTo(LoanStatus.ForApproval, now);
        db.LoanApplications.Add(approval);

        var approved = NewLoan("LAM-OK", now);
        approved.TransitionTo(LoanStatus.ForChecking, now);
        approved.TransitionTo(LoanStatus.ForApproval, now);
        approved.TransitionTo(LoanStatus.Approved, now);
        db.LoanApplications.Add(approved);

        var revision = NewLoan("LAM-REV", now);
        revision.TransitionTo(LoanStatus.ForRevision, now);
        db.LoanApplications.Add(revision);

        await db.SaveChangesAsync();

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(1, overview.PendingRecommendation);
        Assert.Equal(1, overview.PendingEvaluation);
        Assert.Equal(1, overview.PendingApproval);
        Assert.Equal(1, overview.ApprovedToday);
        Assert.Equal(1, overview.Pushbacks);
    }
}
