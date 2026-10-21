using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.SetAvailability;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class SetUserAvailabilityHandlerTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"avail-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task HandleAsync_False_MarksUnavailable()
    {
        await using var db = Db();
        var user = User.Create(
            "u", "hash", "A", "B", "", "a@x.com", "011",
            UserRole.Evaluator, DateTimeOffset.UtcNow, "Credit Checker");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await new SetUserAvailabilityHandler(db, TimeProvider.System)
            .HandleAsync(user.Id, false, CancellationToken.None);

        Assert.False(result.IsAvailable);
        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.False(reloaded.IsAvailable);
    }
}
