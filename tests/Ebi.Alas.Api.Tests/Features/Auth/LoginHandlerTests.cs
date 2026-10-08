using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.Login;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.Seed;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class LoginHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"login-{Guid.NewGuid():N}")
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

    private static LoginHandler Handler(AlasDbContext db) => new(
        db,
        new PasswordHasher(),
        new JwtTokenService(ApiOptions.Jwt),
        new TokenStore(db, TimeProvider.System),
        Options.Create(ApiOptions),
        TimeProvider.System);

    private static async Task SeedAdminAsync(AlasDbContext db, string password)
    {
        var seeder = new AdminUserSeeder(
            db,
            new PasswordHasher(),
            Options.Create(new AdminSeedOptions
            {
                AdminUserName = "admin",
                AdminPassword = password,
                AdminBranchId = "HO",
                AdminFirstName = "System",
                AdminLastName = "Administrator",
                AdminEmail = "admin@example.invalid"
            }),
            TimeProvider.System);
        await seeder.SeedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleAsync_UnknownUser_ReturnsInvalidCredentialsWithoutThrowing()
    {
        await using var db = Db();

        var outcome = await Handler(db).HandleAsync(
            new LoginRequest { UserName = "missing", Password = "whatever-Aa1!" },
            CancellationToken.None);

        var failure = Assert.IsType<LoginOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status401Unauthorized, failure.StatusCode);
        Assert.Equal("Invalid username or password.", failure.Detail);
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_ReturnsInvalidCredentialsWithoutThrowing()
    {
        await using var db = Db();
        await SeedAdminAsync(db, "@Temp123456!");

        var outcome = await Handler(db).HandleAsync(
            new LoginRequest { UserName = "admin", Password = "wrong-password-Aa1!" },
            CancellationToken.None);

        var failure = Assert.IsType<LoginOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status401Unauthorized, failure.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_SeededAdminWithSecretsPassword_Succeeds()
    {
        await using var db = Db();
        await SeedAdminAsync(db, "@Temp123456!");

        var outcome = await Handler(db).HandleAsync(
            new LoginRequest { UserName = "admin", Password = "@Temp123456!" },
            CancellationToken.None);

        var success = Assert.IsType<LoginOutcome.Success>(outcome);
        Assert.False(string.IsNullOrEmpty(success.Response.AccessToken));
        Assert.True(success.Response.MustChangePassword);
    }

    [Fact]
    public async Task HandleAsync_SuspendedUser_ReturnsSuspended()
    {
        await using var db = Db();
        await SeedAdminAsync(db, "@Temp123456!");
        var user = await db.Users.SingleAsync(u => u.UserName == "admin");
        user.Suspend(TimeProvider.System.GetUtcNow());
        await db.SaveChangesAsync();

        var outcome = await Handler(db).HandleAsync(
            new LoginRequest { UserName = "admin", Password = "@Temp123456!" },
            CancellationToken.None);

        var failure = Assert.IsType<LoginOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status403Forbidden, failure.StatusCode);
    }
}
