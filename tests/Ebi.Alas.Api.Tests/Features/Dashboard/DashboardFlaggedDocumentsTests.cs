using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Dashboard;

public sealed class DashboardFlaggedDocumentsTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"dash-flag-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task GetOverview_ReturnsFlaggedLoans()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("enc", "h", "Enc", "One", "M", "e@x.com", "001", UserRole.Encoder, now);
        var flagger = User.Create("fl", "h", "Flag", "Ger", "M", "f@x.com", "001", UserRole.Evaluator, now);
        db.Users.AddRange(encoder, flagger);
        await db.SaveChangesAsync();

        var loan = LoanApplication.Create(
            "LAM-1", "G-1", "Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, encoder.Id, now, "A16", "A16");
        loan.FlagDocuments(2, "Missing IDs", flagger.Id, now);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TimeProvider.System);
        var caller = new CallerContext(flagger.Id, UserRole.Evaluator, "001");

        var overview = await service.GetOverviewAsync(caller, CancellationToken.None);

        var item = Assert.Single(overview.FlaggedDocuments);
        Assert.Equal("LAM-1", item.LamId);
        Assert.Equal(2, item.MissingCount);
        Assert.Equal("Flag Ger", item.FlaggedByName);
        Assert.Equal("Enc One", item.EncoderName);
    }

    [Fact]
    public async Task GetOverview_OmitsUnflaggedLoans()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("enc", "h", "Enc", "One", "M", "e@x.com", "001", UserRole.Encoder, now);
        db.Users.Add(encoder);
        await db.SaveChangesAsync();
        db.LoanApplications.Add(LoanApplication.Create(
            "LAM-1", "G-1", "Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, encoder.Id, now, "A16", "A16"));
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TimeProvider.System);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "001");

        var overview = await service.GetOverviewAsync(caller, CancellationToken.None);

        Assert.Empty(overview.FlaggedDocuments);
    }
}
