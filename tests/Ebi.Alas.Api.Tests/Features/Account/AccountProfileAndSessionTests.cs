using Ebi.Alas.Api.Features.Account;
using Ebi.Alas.Api.Features.Auth.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Account;

public sealed class AccountProfileAndSessionTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"account-sec-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static User SeedUser(AlasDbContext db)
    {
        var user = User.Create("jdoe", "hash", "John", "Doe", "", "j@x.com", "BR-A", UserRole.Encoder, DateTimeOffset.UtcNow);
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task UpdateContact_PersistsEmailPhoneAndEmergencyContact()
    {
        await using var db = Db();
        var user = SeedUser(db);
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        var ok = await service.UpdateContactAsync(
            user.Id,
            email: "new@x.com",
            phone: "09171234567",
            emergencyContact: "Jane Doe — 0918",
            eSignature: null,
            CancellationToken.None);

        Assert.True(ok);
        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal("new@x.com", reloaded.Email);
        Assert.Equal("09171234567", reloaded.Phone);
        Assert.Equal("Jane Doe — 0918", reloaded.EmergencyContact);
    }

    [Fact]
    public async Task UpdateProfile_NullESignature_DoesNotClearStoredInk()
    {
        await using var db = Db();
        var user = SeedUser(db);
        var ink = "data:image/png;base64,iVBORw0KGgo=";
        user.SetSignature(ink, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        await service.UpdateContactAsync(
            user.Id, "a@x.com", null, null, eSignature: null, CancellationToken.None);

        Assert.Equal(ink, (await db.Users.AsNoTracking().SingleAsync()).ESignature);
    }

    [Fact]
    public async Task UpdateProfile_Ink_BackfillsSignedSlotsWithoutSnapshot()
    {
        await using var db = Db();
        var user = SeedUser(db);
        await db.SaveChangesAsync();
        var ink = "data:image/png;base64,iVBORw0KGgo=";
        var entry = Ebi.Alas.Api.Features.Approvals.SignatureChainEntry.Create(
            Guid.NewGuid(), 1, "Encoder", DateTimeOffset.UtcNow);
        entry.Sign(user.Id, DateTimeOffset.UtcNow, signatureImage: null);
        db.SignatureChainEntries.Add(entry);
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        await service.UpdateContactAsync(
            user.Id, null, null, null, eSignature: ink, CancellationToken.None);

        var stored = await db.SignatureChainEntries.AsNoTracking().SingleAsync();
        Assert.Equal(ink, stored.SignatureImage);
    }

    [Fact]
    public async Task GetProfile_IncludesContactFields()
    {
        await using var db = Db();
        var user = SeedUser(db);
        user.UpdateContact("a@x.com", "09170001111", "ICE", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        var profile = await service.GetProfileAsync(user.Id, CancellationToken.None);

        Assert.Equal("a@x.com", profile!.Email);
        Assert.Equal("09170001111", profile.Phone);
        Assert.Equal("ICE", profile.EmergencyContact);
    }

    [Fact]
    public async Task UpdateProfile_PersistsESignature()
    {
        await using var db = Db();
        var user = SeedUser(db);
        await db.SaveChangesAsync();
        var signature = "data:image/png;base64,iVBORw0KGgo=";

        var service = new AccountService(db, TimeProvider.System);
        var ok = await service.UpdateContactAsync(
            user.Id,
            email: "new@x.com",
            phone: null,
            emergencyContact: null,
            eSignature: signature,
            CancellationToken.None);

        Assert.True(ok);
        var profile = await service.GetProfileAsync(user.Id, CancellationToken.None);
        Assert.Equal(signature, profile!.ESignature);
    }

    [Fact]
    public async Task RevokeSession_MarksTokenRevoked()
    {
        await using var db = Db();
        var user = SeedUser(db);
        await db.SaveChangesAsync();
        var token = RefreshToken.Create(user.Id, "hash-1", DateTimeOffset.UtcNow, TimeSpan.FromDays(7), TimeSpan.FromDays(14));
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        var result = await service.RevokeSessionAsync(user.Id, token.Id, currentSessionId: null, CancellationToken.None);

        Assert.Equal(SessionRevokeResult.Revoked, result);
        var reloaded = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id);
        Assert.NotNull(reloaded.RevokedAt);
    }

    [Fact]
    public async Task RevokeOtherSessions_KeepsCurrent()
    {
        await using var db = Db();
        var user = SeedUser(db);
        await db.SaveChangesAsync();
        var current = RefreshToken.Create(user.Id, "hash-cur", DateTimeOffset.UtcNow, TimeSpan.FromDays(7), TimeSpan.FromDays(14));
        var other = RefreshToken.Create(user.Id, "hash-oth", DateTimeOffset.UtcNow, TimeSpan.FromDays(7), TimeSpan.FromDays(14));
        db.RefreshTokens.Add(current);
        db.RefreshTokens.Add(other);
        await db.SaveChangesAsync();

        var service = new AccountService(db, TimeProvider.System);
        var count = await service.RevokeOtherSessionsAsync(user.Id, current.Id, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Null((await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == current.Id)).RevokedAt);
        Assert.NotNull((await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == other.Id)).RevokedAt);
    }
}
