using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.ChangePassword;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class ChangePasswordHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"change-pw-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static readonly ApiOptions ApiOptions = new()
    {
        Jwt = new JwtOptions
        {
            Issuer = "test",
            Audience = "test-clients",
            SigningKey = "unit-test-signing-key-at-least-32-chars!",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
            RefreshTokenAbsoluteDays = 14
        }
    };

    private static ChangePasswordHandler Handler(AlasDbContext db) => new(
        db,
        new PasswordHasher(),
        TimeProvider.System);

    private static async Task<User> SeedUserAsync(AlasDbContext db, string password, bool mustChange = true)
    {
        var hasher = new PasswordHasher();
        var user = User.Create(
            "ada",
            hasher.Hash(password),
            "Ada Lovelace",
            "ada@example.com",
            "011",
            UserRole.Encoder,
            DateTimeOffset.UtcNow);
        if (!mustChange)
        {
            user.ClearMustChangePassword(TimeProvider.System.GetUtcNow());
        }
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task HandleAsync_WrongCurrentPassword_Fails()
    {
        await using var db = Db();
        var user = await SeedUserAsync(db, "OldPass!1");

        var result = await Handler(db).HandleAsync(
            user.Id,
            new ChangePasswordRequest { CurrentPassword = "WrongPass!1", NewPassword = "NewPass!2" },
            CancellationToken.None);

        Assert.IsType<ChangePasswordOutcome.Failure>(result);
    }

    [Fact]
    public async Task HandleAsync_ValidCurrentPassword_SetsNewHashAndClearsMustChange()
    {
        await using var db = Db();
        var user = await SeedUserAsync(db, "OldPass!1", mustChange: true);
        var hasher = new PasswordHasher();

        var result = await Handler(db).HandleAsync(
            user.Id,
            new ChangePasswordRequest { CurrentPassword = "OldPass!1", NewPassword = "NewPass!2" },
            CancellationToken.None);

        Assert.IsType<ChangePasswordOutcome.Success>(result);

        var updated = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.False(updated.MustChangePassword);
        Assert.True(hasher.Verify("NewPass!2", updated.PasswordHash));
        Assert.False(hasher.Verify("OldPass!1", updated.PasswordHash));
    }

    [Fact]
    public async Task HandleAsync_RevokesExistingRefreshTokens()
    {
        await using var db = Db();
        var user = await SeedUserAsync(db, "OldPass!1");
        var store = new TokenStore(db, TimeProvider.System);
        var (token, _) = await store.IssueRefreshTokenAsync(
            user.Id,
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            CancellationToken.None);

        await Handler(db).HandleAsync(
            user.Id,
            new ChangePasswordRequest { CurrentPassword = "OldPass!1", NewPassword = "NewPass!2" },
            CancellationToken.None);

        var stored = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id);
        Assert.False(stored.IsUsable(TimeProvider.System.GetUtcNow()));
    }
}
