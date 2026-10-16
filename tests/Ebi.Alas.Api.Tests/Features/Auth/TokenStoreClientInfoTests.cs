using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class TokenStoreClientInfoTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"token-client-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task IssueRefreshTokenAsync_PersistsIpAndUserAgent()
    {
        await using var db = Db();
        var store = new TokenStore(db, TimeProvider.System);
        var userId = Guid.NewGuid();

        var (token, _) = await store.IssueRefreshTokenAsync(
            userId,
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            new SessionClientInfo("203.0.113.5", "UnitTestAgent/1.0", "UnitTestAgent"),
            CancellationToken.None);

        Assert.Equal("203.0.113.5", token.IpAddress);
        Assert.Equal("UnitTestAgent/1.0", token.UserAgent);
    }

    [Fact]
    public async Task RotateAsync_CopiesClientInfoOntoNewToken()
    {
        await using var db = Db();
        var store = new TokenStore(db, TimeProvider.System);
        var userId = Guid.NewGuid();
        var (original, _) = await store.IssueRefreshTokenAsync(
            userId,
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            new SessionClientInfo("203.0.113.5", "UnitTestAgent/1.0", "UnitTestAgent"),
            CancellationToken.None);

        var (rotated, _) = await store.RotateAsync(
            original,
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            new SessionClientInfo("203.0.113.6", "UnitTestAgent/2.0", "UnitTestAgent"),
            CancellationToken.None);

        Assert.Equal("203.0.113.6", rotated.IpAddress);
        Assert.Equal("UnitTestAgent/2.0", rotated.UserAgent);
    }
}
