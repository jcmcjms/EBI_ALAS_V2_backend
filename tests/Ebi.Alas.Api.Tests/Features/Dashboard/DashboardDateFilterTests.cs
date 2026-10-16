using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Dashboard;

public sealed class DashboardDateFilterTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"dash-date-{Guid.NewGuid():N}")
            .Options);

    private static LoanApplication NewLoan(
        string lamId,
        string branchId,
        DateTimeOffset now,
        Guid createdBy) =>
        LoanApplication.Create(
            lamId, "GRP-1", "Client", branchId, LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, createdBy, now);

    [Fact]
    public void ManilaDayRange_CoversLocalCalendarDay()
    {
        var (start, end) = DashboardService.ManilaDayRange(new DateOnly(2026, 10, 9));
        Assert.Equal(new DateOnly(2026, 10, 9), DateOnly.FromDateTime(start.DateTime));
        Assert.Equal(start.AddDays(1), end);
    }

    [Fact]
    public async Task GetOverview_PushbacksOnlyIncludeSelectedDay()
    {
        await using var db = Db();
        var encoderId = Guid.NewGuid();
        var selected = new DateOnly(2026, 10, 9);
        var (dayStart, dayEnd) = DashboardService.ManilaDayRange(selected);
        var midDay = dayStart.AddHours(3);
        var priorDay = dayStart.AddDays(-1).AddHours(3);

        var returnedToday = NewLoan("LAM-TODAY", "BR-01", midDay, encoderId);
        returnedToday.TransitionTo(LoanStatus.ForRevision, midDay);

        var returnedYesterday = NewLoan("LAM-YDAY", "BR-01", priorDay, encoderId);
        returnedYesterday.TransitionTo(LoanStatus.ForRevision, priorDay);

        db.LoanApplications.AddRange(returnedToday, returnedYesterday);
        await db.SaveChangesAsync();

        var service = new DashboardService(db);
        var caller = new CallerContext(encoderId, UserRole.Admin, "BR-01");
        var overview = await service.GetOverviewAsync(caller, selected, CancellationToken.None);

        Assert.Equal(1, overview.Pushbacks);
        Assert.Contains(overview.PushBackItems, i => i.LamId == "LAM-TODAY");
        Assert.DoesNotContain(overview.PushBackItems, i => i.LamId == "LAM-YDAY");
    }

    [Fact]
    public async Task GetOverview_DateParamSelectsHistoricalDay()
    {
        await using var db = Db();
        var encoderId = Guid.NewGuid();
        var selected = new DateOnly(2026, 10, 8);
        var (dayStart, _) = DashboardService.ManilaDayRange(selected);
        var midDay = dayStart.AddHours(3);

        var historical = NewLoan("LAM-HIST", "BR-01", midDay, encoderId);
        historical.TransitionTo(LoanStatus.ForRevision, midDay);
        db.LoanApplications.Add(historical);
        await db.SaveChangesAsync();

        var service = new DashboardService(db);
        var caller = new CallerContext(encoderId, UserRole.Admin, "BR-01");

        var onDay = await service.GetOverviewAsync(caller, selected, CancellationToken.None);
        Assert.Equal(1, onDay.Pushbacks);

        var onOther = await service.GetOverviewAsync(
            caller, selected.AddDays(1), CancellationToken.None);
        Assert.Equal(0, onOther.Pushbacks);
    }

    [Fact]
    public async Task GetOverview_ApprovedCountUsesSelectedDay()
    {
        await using var db = Db();
        var encoderId = Guid.NewGuid();
        var selected = new DateOnly(2026, 10, 9);
        var (dayStart, _) = DashboardService.ManilaDayRange(selected);
        var midDay = dayStart.AddHours(3);

        var approved = NewLoan("LAM-OK", "BR-01", midDay, encoderId);
        approved.TransitionTo(LoanStatus.ForChecking, midDay);
        approved.TransitionTo(LoanStatus.ForApproval, midDay);
        approved.TransitionTo(LoanStatus.Approved, midDay);
        db.LoanApplications.Add(approved);
        await db.SaveChangesAsync();

        var service = new DashboardService(db);
        var caller = new CallerContext(encoderId, UserRole.Admin, "BR-01");
        var overview = await service.GetOverviewAsync(caller, selected, CancellationToken.None);

        Assert.Equal(1, overview.ApprovedToday);
        Assert.Contains(overview.ApprovedLoans, a => a.LamId == "LAM-OK");
    }
}
