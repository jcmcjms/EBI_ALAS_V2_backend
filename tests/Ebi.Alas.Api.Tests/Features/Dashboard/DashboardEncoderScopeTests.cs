using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Dashboard;

public sealed class DashboardEncoderScopeTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"dash-enc-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication NewLoan(
        string lamId,
        string branchId,
        DateTimeOffset now,
        Guid? createdBy = null) =>
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
            createdBy ?? Guid.NewGuid(),
            now);

    [Fact]
    public async Task GetOverviewAsync_Encoder_QueuesFilteredToOwnLoans_SummaryRemainsBranchWide()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new DashboardService(db);
        var encoderId = Guid.NewGuid();

        var mine = NewLoan("LAM-MINE", "BR-01", now, createdBy: encoderId);
        var other = NewLoan("LAM-OTHER", "BR-01", now, createdBy: Guid.NewGuid());

        var mineRevision = NewLoan("LAM-MINE-REV", "BR-01", now, createdBy: encoderId);
        mineRevision.TransitionTo(LoanStatus.ForRevision, now);

        var otherRevision = NewLoan("LAM-OTHER-REV", "BR-01", now, createdBy: Guid.NewGuid());
        otherRevision.TransitionTo(LoanStatus.ForRevision, now);

        db.LoanApplications.AddRange(mine, other, mineRevision, otherRevision);
        await db.SaveChangesAsync();

        var caller = new CallerContext(encoderId, UserRole.Encoder, "BR-01");
        var overview = await service.GetOverviewAsync(caller, new DateOnly(2026, 10, 9), CancellationToken.None);

        Assert.Equal(2, overview.PendingRecommendation);
        Assert.Equal(2, overview.Pushbacks);

        Assert.Contains(overview.PendingQueue, i => i.LamId == "LAM-MINE");
        Assert.DoesNotContain(overview.PendingQueue, i => i.LamId == "LAM-OTHER");
        Assert.Contains(overview.PushBackItems, i => i.LamId == "LAM-MINE-REV");
        Assert.DoesNotContain(overview.PushBackItems, i => i.LamId == "LAM-OTHER-REV");
    }

    [Fact]
    public async Task GetOverviewAsync_Recommender_SeesBranchQueues()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new DashboardService(db);

        db.LoanApplications.Add(NewLoan("LAM-A", "BR-01", now, createdBy: Guid.NewGuid()));
        db.LoanApplications.Add(NewLoan("LAM-B", "BR-01", now, createdBy: Guid.NewGuid()));
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "BR-01");
        var overview = await service.GetOverviewAsync(caller, new DateOnly(2026, 10, 9), CancellationToken.None);

        Assert.Equal(2, overview.PendingRecommendation);
        Assert.Equal(2, overview.PendingQueue.Count);
    }
}
