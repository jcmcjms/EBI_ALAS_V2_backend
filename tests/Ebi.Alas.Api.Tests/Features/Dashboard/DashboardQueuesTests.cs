using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Dashboard;

public sealed class DashboardQueuesTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"dash-q-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task GetOverview_ReturnsPendingApprovedAndTrend()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("enc", "h", "Enc", "One", "M", "e@x.com", "001", UserRole.Encoder, now);
        db.Users.Add(encoder);
        await db.SaveChangesAsync();

        var pending = LoanApplication.Create(
            "LAM-1", "G-1", "Pending Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, encoder.Id, now, "A16", "A16");
        var approved = LoanApplication.Create(
            "LAM-2", "G-1", "Approved Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, encoder.Id, now, "A16", "A16");
        approved.TransitionTo(LoanStatus.ForChecking, now);
        approved.TransitionTo(LoanStatus.ForApproval, now);
        approved.TransitionTo(LoanStatus.Approved, now, encoder.Id, "Approved");
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            approved.Id, encoder.Id, "Approved", "ForApproval", "Approved", null, now));
        db.LoanApplications.AddRange(pending, approved);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TimeProvider.System);
        var caller = new CallerContext(encoder.Id, UserRole.Admin, "001");

        var overview = await service.GetOverviewAsync(caller, CancellationToken.None);

        Assert.Contains(overview.PendingQueue, q => q.LamId == "LAM-1");
        Assert.Contains(overview.ApprovedLoans, a => a.LamId == "LAM-2");
        Assert.Equal(1, overview.WeeklyTrend.Sum(t => t.Approved));
    }
}
