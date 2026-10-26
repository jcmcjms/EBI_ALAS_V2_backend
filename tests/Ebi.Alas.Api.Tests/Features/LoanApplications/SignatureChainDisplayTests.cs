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

        var slots = await SignatureChainService.GetDisplayChainAsync(
            db, loanId, requireRecommendation: false, CancellationToken.None);

        Assert.Equal(3, slots.Count);
        Assert.Equal(["Encode", "Evaluate", "Approve"], slots.Select(s => s.Action).ToArray());
        Assert.All(slots, s => Assert.Null(s.SignedByName));
    }

    [Fact]
    public async Task GetDisplayChain_WhenRecommendationRequired_IncludesRecommender()
    {
        await using var db = Db();
        var loanId = Guid.NewGuid();

        var slots = await SignatureChainService.GetDisplayChainAsync(
            db, loanId, requireRecommendation: true, CancellationToken.None);

        Assert.Equal(4, slots.Count);
        Assert.Equal(
            ["Encode", "Recommend", "Evaluate", "Approve"],
            slots.Select(s => s.Action).ToArray());
    }

    [Fact]
    public async Task GetDisplayChain_FillsSignerNameAfterSign()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var recommender = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Recommender, now, "Branch Head");
        recommender.SetSignature(Users.SignatureTestImages.CreatePngDataUrl(), now);
        var loan = LoanApplication.Create(
            "LAM-SIG", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);
        db.Users.Add(recommender);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        await SignatureChainService.EnsureCreatedAsync(
            db, loan.Id, requireRecommendation: true, now, CancellationToken.None);

        await SignatureChainService.SignAsync(db, loan.Id, "Recommender", recommender.Id, now, CancellationToken.None);

        var slots = await SignatureChainService.GetDisplayChainAsync(
            db, loan.Id, requireRecommendation: true, CancellationToken.None);
        var signed = slots.Single(s => s.Role == "Recommender");
        Assert.Equal("Ana Cruz", signed.SignedByName);
        Assert.NotNull(signed.SignedAt);
        Assert.Null(slots.Single(s => s.Role == "Evaluator").SignedByName);
    }

    [Fact]
    public async Task Sign_RequiresSavedSignature()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var recommender = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Recommender, now, "Branch Head");
        var loan = LoanApplication.Create(
            "LAM-NOINK", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);
        db.Users.Add(recommender);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        await SignatureChainService.EnsureCreatedAsync(
            db, loan.Id, requireRecommendation: true, now, CancellationToken.None);

        await Assert.ThrowsAsync<Ebi.Alas.Api.Composition.Errors.ValidationException>(() =>
            SignatureChainService.SignAsync(db, loan.Id, "Recommender", recommender.Id, now, CancellationToken.None));
    }

    [Fact]
    public async Task Sign_SnapshotsInkAndDisplayExposesIt()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var ink = Users.SignatureTestImages.CreatePngDataUrl();
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Account Officer");
        encoder.SetSignature(ink, now);
        var loan = LoanApplication.Create(
            "LAM-INK", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        db.Users.Add(encoder);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        await SignatureChainService.EnsureCreatedAsync(
            db, loan.Id, requireRecommendation: false, now, CancellationToken.None);

        await SignatureChainService.SignAsync(db, loan.Id, "Encoder", encoder.Id, now, CancellationToken.None);

        var slots = await SignatureChainService.GetDisplayChainAsync(
            db, loan.Id, requireRecommendation: false, CancellationToken.None);
        Assert.NotNull(slots.Single(s => s.Role == "Encoder").SignatureUrl);
        Assert.StartsWith("data:image/png;base64,", slots.Single(s => s.Role == "Encoder").SignatureUrl);
        Assert.Null(slots.Single(s => s.Role == "Evaluator").SignatureUrl);
    }

    [Fact]
    public void Sign_IgnoresUnsafeInkSnapshot()
    {
        var entry = Ebi.Alas.Api.Features.Approvals.SignatureChainEntry.Create(
            Guid.NewGuid(), 1, "Encoder", DateTimeOffset.UtcNow);
        entry.Sign(Guid.NewGuid(), DateTimeOffset.UtcNow, "data:image/svg+xml;base64,PHN2Zz4=");
        Assert.Null(entry.SignatureImage);
    }
}
