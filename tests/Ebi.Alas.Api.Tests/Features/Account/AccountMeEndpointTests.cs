using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Account;

public sealed class AccountMeEndpointTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"account-me-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static User SeedUser(AlasDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("jdoe", "hash", "John", "Doe", "Q", "j@x.com", "BR-A", UserRole.Encoder, now);
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task GetProfile_ReturnsUserAndLoanStats()
    {
        await using var db = Db();
        var user = SeedUser(db);
        var now = DateTimeOffset.UtcNow;
        var created = LoanApplication.Create(
            "LAM-A", "G-1", "Client A", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, user.Id, now);
        created.TransitionTo(LoanStatus.ForChecking, now);
        var approved = LoanApplication.Create(
            "LAM-B", "G-2", "Client B", "BR-A", LoanType.New,
            2000m, 30, 0.05m, 50m, 10m, 1940m, user.Id, now);
        approved.TransitionTo(LoanStatus.ForChecking, now);
        approved.TransitionTo(LoanStatus.ForApproval, now);
        approved.TransitionTo(LoanStatus.Approved, now);
        db.LoanApplications.Add(created);
        db.LoanApplications.Add(approved);
        await db.SaveChangesAsync();

        var service = new Ebi.Alas.Api.Features.Account.AccountService(db, TimeProvider.System);
        var profile = await service.GetProfileAsync(user.Id, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("John", profile!.FirstName);
        Assert.Equal("Doe", profile.LastName);
        Assert.Equal("BR-A", profile.BranchId);
        Assert.Equal(2, profile.Stats.ProcessedLoans);
        Assert.Equal(1, profile.Stats.PendingLoans);
        Assert.Equal(50, profile.Stats.ApprovalRate);
    }

    [Fact]
    public async Task GetRecentActivity_ReturnsUserLoanActions()
    {
        await using var db = Db();
        var user = SeedUser(db);
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-C", "G-1", "Client C", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, user.Id, now);
        db.LoanApplications.Add(loan);
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, user.Id, "Recommended", null, "ForChecking", "ok", now));
        await db.SaveChangesAsync();

        var service = new Ebi.Alas.Api.Features.Account.AccountService(db, TimeProvider.System);
        var activity = await service.GetRecentActivityAsync(user.Id, 10, CancellationToken.None);

        var item = Assert.Single(activity);
        Assert.Equal("LAM-C", item.LamId);
        Assert.Equal("Recommended", item.Action);
        Assert.Equal("Client C", item.LoanClientName);
    }

    [Fact]
    public async Task GetProcessedLoansAndClients_UseCreatedBy()
    {
        await using var db = Db();
        var user = SeedUser(db);
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-D", "G-1", "Client D", "BR-A", LoanType.New,
            1500m, 30, 0.05m, 50m, 10m, 1440m, user.Id, now);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var service = new Ebi.Alas.Api.Features.Account.AccountService(db, TimeProvider.System);
        var loans = await service.GetProcessedLoansAsync(user.Id, 10, CancellationToken.None);
        var clients = await service.GetRecentClientsAsync(user.Id, 5, CancellationToken.None);

        Assert.Equal("LAM-D", Assert.Single(loans).LamId);
        Assert.Equal("Client D", Assert.Single(clients).Name);
    }
}
