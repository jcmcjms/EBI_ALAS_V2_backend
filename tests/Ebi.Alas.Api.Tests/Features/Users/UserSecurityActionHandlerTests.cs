using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.ForcePasswordReset;
using Ebi.Alas.Api.Features.Users.ResetPassword;
using Ebi.Alas.Api.Features.Users.RevokeSessions;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class UserSecurityActionHandlerTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"sec-{Guid.NewGuid():N}")
            .Options);

    private static User SeedUser(AlasDbContext db)
    {
        var user = User.Create(
            "u", "hash", "A", "B", "", "a@x.com", "011",
            UserRole.Evaluator, DateTimeOffset.UtcNow, "Credit Checker");
        user.ClearMustChangePassword(DateTimeOffset.UtcNow);
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task ResetPassword_GeneratesTemporaryPasswordAndRevokesTokens()
    {
        await using var db = Db();
        var user = SeedUser(db);
        var token = RefreshToken.Create(
            user.Id, "token-hash", DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromDays(30));
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        var result = await new ResetPasswordHandler(db, new PasswordHasher(), TimeProvider.System)
            .HandleAsync(user.Id, CancellationToken.None);

        Assert.Equal(user.UserName, result.Username);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        Assert.True(result.MustChangePassword);

        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(reloaded.MustChangePassword);
        Assert.True(new PasswordHasher().Verify(result.TemporaryPassword, reloaded.PasswordHash));

        var reloadedToken = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id);
        Assert.NotNull(reloadedToken.RevokedAt);
    }

    [Fact]
    public async Task ForcePasswordReset_SetsMustChangePassword()
    {
        await using var db = Db();
        var user = SeedUser(db);
        await db.SaveChangesAsync();

        var result = await new ForcePasswordResetHandler(db, TimeProvider.System)
            .HandleAsync(user.Id, CancellationToken.None);

        Assert.True(result.MustChangePassword);
    }

    [Fact]
    public async Task RevokeSessions_RevokesActiveTokensOnly()
    {
        await using var db = Db();
        var user = SeedUser(db);
        var active = RefreshToken.Create(
            user.Id, "a", DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromDays(30));
        var revoked = RefreshToken.Create(
            user.Id, "r", DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromDays(30));
        revoked.Revoke(DateTimeOffset.UtcNow, Guid.Empty);
        db.RefreshTokens.AddRange(active, revoked);
        await db.SaveChangesAsync();

        var result = await new RevokeSessionsHandler(db, TimeProvider.System)
            .HandleAsync(user.Id, CancellationToken.None);

        Assert.Equal(1, result.RevokedCount);
        var reloaded = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == active.Id);
        Assert.NotNull(reloaded.RevokedAt);
    }
}
