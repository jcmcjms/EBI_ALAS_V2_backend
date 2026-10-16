using Ebi.Alas.Api.Features.Account;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Account;

public sealed class AccountActivityFilterTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"act-filter-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task<(User user, LoanApplication loan)> SeedAsync(AlasDbContext db)
    {
        var user = User.Create("act", "hash", "A", "B", "", "a@x.com", "BR-1", UserRole.Encoder, DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var loan = LoanApplication.Create(
            "LAM-1",
            "GRP-1",
            "Ada",
            "BR-1",
            LoanType.New,
            10000m,
            60,
            2.5m,
            500m,
            100m,
            9900m,
            user.Id,
            DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        return (user, loan);
    }

    [Fact]
    public async Task GetRecentActivity_FlagsInformalComments()
    {
        await using var db = Db();
        var (user, loan) = await SeedAsync(db);
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, user.Id, "Submit", null, null, "test note", DateTimeOffset.UtcNow));
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, user.Id, "Submit", null, null, "Client confirmed", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        var rows = await service.GetRecentActivityAsync(user.Id, 10, null, null, null, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.IsInformal);
        Assert.Contains(rows, r => !r.IsInformal);
    }

    [Fact]
    public async Task GetRecentActivity_FiltersByActionKind()
    {
        await using var db = Db();
        var (user, loan) = await SeedAsync(db);
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, user.Id, "Change password", null, null, null, DateTimeOffset.UtcNow));
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, user.Id, "Submit for recommendation", null, null, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        var security = await service.GetRecentActivityAsync(user.Id, 10, null, null, "security", CancellationToken.None);

        Assert.All(security, r => Assert.Contains("password", r.Action, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetRecentActivity_FiltersByDateRange()
    {
        await using var db = Db();
        var (user, loan) = await SeedAsync(db);
        var oldDate = DateTimeOffset.UtcNow.AddDays(-30);
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, user.Id, "Submit", null, null, null, oldDate));
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, user.Id, "Submit", null, null, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await service.GetRecentActivityAsync(user.Id, 10, today, today, null, CancellationToken.None);

        Assert.Single(rows);
    }

    [Fact]
    public async Task GetRecentActivity_FromAfterTo_Throws()
    {
        await using var db = Db();
        var (user, _) = await SeedAsync(db);
        var service = new AccountService(db, TimeProvider.System);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await Assert.ThrowsAsync<Ebi.Alas.Api.Composition.Errors.ValidationException>(() =>
            service.GetRecentActivityAsync(user.Id, 10, today, today.AddDays(-1), null, CancellationToken.None));
    }
}
