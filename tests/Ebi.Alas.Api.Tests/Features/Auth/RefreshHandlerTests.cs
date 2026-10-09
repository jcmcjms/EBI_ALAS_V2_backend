using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.Refresh;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class RefreshHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"refresh-{Guid.NewGuid():N}")
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

    private static RefreshHandler Handler(AlasDbContext db) => new(
        db,
        new JwtTokenService(ApiOptions.Jwt),
        new TokenStore(db, TimeProvider.System),
        Options.Create(ApiOptions),
        TimeProvider.System,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<RefreshHandler>.Instance);

    private static async Task<(User User, string Raw)> SeedAsync(AlasDbContext db)
    {
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

        var store = new TokenStore(db, TimeProvider.System);
        var (_, raw) = await store.IssueRefreshTokenAsync(
            user.Id,
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            CancellationToken.None);
        return (user, raw);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_ReturnsNewAccessTokenAndRotatedRefresh()
    {
        await using var db = Db();
        var (_, raw) = await SeedAsync(db);

        var outcome = await Handler(db).HandleAsync(
            new RefreshRequest { RefreshToken = raw },
            CancellationToken.None);

        var success = Assert.IsType<RefreshOutcome.Success>(outcome);
        Assert.False(string.IsNullOrEmpty(success.Response.AccessToken));
        Assert.False(string.IsNullOrEmpty(success.Response.RefreshToken));
        Assert.NotEqual(raw, success.Response.RefreshToken);
    }

    [Fact]
    public async Task HandleAsync_MissingToken_ReturnsInvalidTokenOutcome()
    {
        await using var db = Db();

        var outcome = await Handler(db).HandleAsync(new RefreshRequest(), CancellationToken.None);

        var failure = Assert.IsType<RefreshOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status401Unauthorized, failure.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_UnknownToken_ReturnsInvalidTokenOutcome()
    {
        await using var db = Db();

        var outcome = await Handler(db).HandleAsync(
            new RefreshRequest { RefreshToken = "not-a-real-token" },
            CancellationToken.None);

        var failure = Assert.IsType<RefreshOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status401Unauthorized, failure.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_Rotation_RevokesOldToken()
    {
        await using var db = Db();
        var (_, raw) = await SeedAsync(db);

        await Handler(db).HandleAsync(
            new RefreshRequest { RefreshToken = raw },
            CancellationToken.None);

        var store = new TokenStore(db, TimeProvider.System);
        Assert.Null(await store.FindUsableAsync(raw, CancellationToken.None));
    }
}
