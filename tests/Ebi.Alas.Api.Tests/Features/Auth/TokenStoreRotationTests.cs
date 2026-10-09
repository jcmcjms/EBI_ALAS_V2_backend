using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class TokenStoreRotationTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"rotate-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task RotateAsync_RevokesOldIssuesNew_AndLinksLineage()
    {
        await using var db = Db();
        var store = new TokenStore(db, TimeProvider.System);
        var user = User.Create(
            "ada",
            new PasswordHasher().Hash("Str0ngPass!"),
            "Ada",
            "ada@example.com",
            "011",
            UserRole.Encoder,
            DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var (old, _) = await store.IssueRefreshTokenAsync(
            user.Id, TimeSpan.FromDays(7), TimeSpan.FromDays(14), CancellationToken.None);

        var (next, raw) = await store.RotateAsync(
            old, TimeSpan.FromDays(7), TimeSpan.FromDays(14), CancellationToken.None);

        Assert.NotEqual(old.Id, next.Id);
        Assert.False(string.IsNullOrEmpty(raw));

        var storedOld = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == old.Id);
        var storedNew = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == next.Id);

        Assert.False(storedOld.IsUsable(TimeProvider.System.GetUtcNow()));
        Assert.Equal(next.Id, storedOld.ReplacedById);
        Assert.True(storedNew.IsUsable(TimeProvider.System.GetUtcNow()));
    }
}
