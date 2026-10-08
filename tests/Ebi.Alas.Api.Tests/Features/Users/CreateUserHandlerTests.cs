using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users;
using Ebi.Alas.Api.Features.Users.CreateUser;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class CreateUserHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"create-user-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static CreateUserHandler Handler(AlasDbContext db) =>
        new(db, new PasswordHasher(), TimeProvider.System);

    private static CreateUserRequest Request(string userName = "new-user", string password = "StrongPass!234") =>
        new(userName, password, "Full Name", "user@example.invalid", "BR-01", UserRole.Encoder);

    [Fact]
    public async Task HandleAsync_ReturnsUserWithoutPasswordInPayload()
    {
        await using var db = Db();

        var outcome = await Handler(db).HandleAsync(Request(), CancellationToken.None);

        var success = Assert.IsType<CreateUserOutcome.Success>(outcome);
        Assert.Equal("new-user", success.User.UserName);
        Assert.True(success.User.MustChangePassword);
    }

    [Fact]
    public async Task HandleAsync_DuplicateUsername_ReturnsConflictWithoutThrowing()
    {
        await using var db = Db();
        var handler = Handler(db);
        await handler.HandleAsync(Request(), CancellationToken.None);

        var outcome = await handler.HandleAsync(Request(), CancellationToken.None);

        var failure = Assert.IsType<CreateUserOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status409Conflict, failure.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_MissingPassword_ReturnsBadRequestWithoutThrowing()
    {
        await using var db = Db();

        var outcome = await Handler(db).HandleAsync(Request(password: "  "), CancellationToken.None);

        var failure = Assert.IsType<CreateUserOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_ShortPassword_ReturnsBadRequest()
    {
        await using var db = Db();

        var outcome = await Handler(db).HandleAsync(Request(password: "short"), CancellationToken.None);

        var failure = Assert.IsType<CreateUserOutcome.Failure>(outcome);
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_DoesNotStorePlaintextPassword()
    {
        await using var db = Db();
        await Handler(db).HandleAsync(Request(password: "StrongPass!234"), CancellationToken.None);

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "new-user");
        Assert.NotEqual("StrongPass!234", user.PasswordHash);
        Assert.True(new PasswordHasher().Verify("StrongPass!234", user.PasswordHash));
    }
}
