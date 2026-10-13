using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Branches;

public sealed class ListBranchesHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"branch-list-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task SeedAsync(AlasDbContext db, BranchStatus secondStatus)
    {
        var first = Branch.Create("011", "Head Office", "NA", DateTimeOffset.UtcNow);
        var second = Branch.Create("012", "Cagayan", "NA", DateTimeOffset.UtcNow);
        second.ChangeStatus(secondStatus, DateTimeOffset.UtcNow);
        db.Branches.Add(first);
        db.Branches.Add(second);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_NullStatus_ReturnsAll()
    {
        await using var db = Db();
        await SeedAsync(db, BranchStatus.Closed);

        var handler = new ListBranchesHandler(db);
        var result = await handler.HandleAsync(null, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task HandleAsync_ActiveFilter_ExcludesClosed()
    {
        await using var db = Db();
        await SeedAsync(db, BranchStatus.Closed);

        var handler = new ListBranchesHandler(db);
        var result = await handler.HandleAsync("active", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("011", result[0].Code);
    }

    [Fact]
    public async Task HandleAsync_AllFilter_ReturnsInactiveToo()
    {
        await using var db = Db();
        await SeedAsync(db, BranchStatus.Merged);

        var handler = new ListBranchesHandler(db);
        var result = await handler.HandleAsync("all", CancellationToken.None);

        Assert.Equal(2, result.Count);
    }
}
