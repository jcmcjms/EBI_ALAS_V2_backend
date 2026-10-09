using Ebi.Alas.Api.Features.Users;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.UpdateUser;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class UpdateUserHandlerTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"update-user-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task HandleAsync_PersistsFullNameEmailBranchAndRole()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("jdoe", "hash", "John Doe", "old@x.com", "011", UserRole.Encoder, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateUserHandler(db, new FixedTimeProvider(now));
        var result = await handler.HandleAsync(
            user.Id,
            new UpdateUserRequest("Jane Smith", "jane@x.com", "020", UserRole.Approver),
            CancellationToken.None);

        Assert.Equal("Jane Smith", result.FullName);
        Assert.Equal("jane@x.com", result.Email);
        Assert.Equal("020", result.BranchId);
        Assert.Equal(nameof(UserRole.Approver), result.Role);

        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal("Jane Smith", reloaded.FullName);
        Assert.Equal("020", reloaded.BranchId);
        Assert.Equal(UserRole.Approver, reloaded.Role);
    }

    [Fact]
    public async Task HandleAsync_NullEmail_StoresEmptyEmail()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("jdoe", "hash", "John Doe", "old@x.com", "011", UserRole.Encoder, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateUserHandler(db, new FixedTimeProvider(now));
        var result = await handler.HandleAsync(
            user.Id,
            new UpdateUserRequest("John Doe", null, "011", UserRole.Encoder),
            CancellationToken.None);

        Assert.Equal(string.Empty, result.Email);
    }
}
