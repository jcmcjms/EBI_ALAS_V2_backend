using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class SignatureChainDisplayTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"sig-chain-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task GetDisplayChain_AlwaysReturnsThreePrintableSlots()
    {
        await using var db = Db();
        var loanId = Guid.NewGuid();

        var slots = await SignatureChainService.GetDisplayChainAsync(db, loanId, CancellationToken.None);

        Assert.Equal(3, slots.Count);
        Assert.Equal(["Recommend", "Evaluate", "Approve"], slots.Select(s => s.Action).ToArray());
        Assert.All(slots, s => Assert.Null(s.SignedByName));
    }

    [Fact]
    public async Task GetDisplayChain_FillsSignerNameAfterSign()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var recommender = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Recommender, now, "Branch Head");
        var loan = LoanApplication.Create(
            "LAM-SIG", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);
        db.Users.Add(recommender);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        await SignatureChainService.EnsureCreatedAsync(db, loan.Id, now, CancellationToken.None);

        await SignatureChainService.SignAsync(db, loan.Id, "Recommender", recommender.Id, now, CancellationToken.None);

        var slots = await SignatureChainService.GetDisplayChainAsync(db, loan.Id, CancellationToken.None);
        Assert.Equal("Ana Cruz", slots[0].SignedByName);
        Assert.NotNull(slots[0].SignedAt);
        Assert.Null(slots[1].SignedByName);
    }
}
