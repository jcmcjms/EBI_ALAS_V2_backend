using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.ChangePassword;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class ChangePasswordMinLengthTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"change-pw-min-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task<User> SeedAsync(AlasDbContext db)
    {
        var hasher = new PasswordHasher();
        var user = User.Create(
            "minlen",
            hasher.Hash("OldPass!12"),
            "Min",
            "Len",
            "",
            "min@example.invalid",
            "BR-01",
            UserRole.Encoder,
            DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static ChangePasswordHandler Handler(AlasDbContext db) => new(
        db,
        new PasswordHasher(),
        TimeProvider.System,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ChangePasswordHandler>.Instance);

    [Fact]
    public void MinPasswordLength_IsTwelve()
    {
        Assert.Equal(12, ChangePasswordRequest.MinPasswordLength);
    }

    [Fact]
    public async Task HandleAsync_ElevenCharPassword_Fails()
    {
        await using var db = Db();
        var user = await SeedAsync(db);

        var result = await Handler(db).HandleAsync(
            user.Id,
            new ChangePasswordRequest
            {
                CurrentPassword = "OldPass!12",
                NewPassword = "ShortPass1!",
            },
            CancellationToken.None);

        var failure = Assert.IsType<ChangePasswordOutcome.Failure>(result);
        Assert.Contains("12", failure.Detail);
    }

    [Fact]
    public async Task HandleAsync_TwelveCharPassword_Succeeds()
    {
        await using var db = Db();
        var user = await SeedAsync(db);

        var result = await Handler(db).HandleAsync(
            user.Id,
            new ChangePasswordRequest
            {
                CurrentPassword = "OldPass!12",
                NewPassword = "LongEnoughPw1",
            },
            CancellationToken.None);

        Assert.IsType<ChangePasswordOutcome.Success>(result);
    }
}
