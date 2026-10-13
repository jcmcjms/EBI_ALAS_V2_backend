using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Branches;

public sealed class CreateBranchHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"branch-create-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task HandleAsync_CreatesActiveBranch()
    {
        await using var db = Db();
        var handler = new CreateBranchHandler(db);

        var result = await handler.HandleAsync(
            new CreateBranchRequest("040", "New Branch", "MINDANAO"),
            CancellationToken.None);

        Assert.Equal("040", result.Code);
        Assert.Equal("New Branch", result.Name);
        Assert.Equal("MINDANAO", result.AreaCode);
        Assert.Equal("Active", result.Status);
    }

    [Fact]
    public async Task HandleAsync_DuplicateCode_ThrowsInvalidOperation()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("040", "Existing", "NA", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var handler = new CreateBranchHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateBranchRequest("040", "Other", "NA"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateHandler_ChangesNameAndArea()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("040", "Old Name", "NA", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var handler = new UpdateBranchHandler(db);
        var result = await handler.HandleAsync(
            "040", new UpdateBranchRequest("New Name", "VISAYAS"), CancellationToken.None);

        Assert.Equal("040", result.Code);
        Assert.Equal("New Name", result.Name);
        Assert.Equal("VISAYAS", result.AreaCode);
        Assert.Equal("Active", result.Status);
    }

    [Fact]
    public async Task UpdateHandler_MissingCode_ThrowsNotFound()
    {
        await using var db = Db();
        var handler = new UpdateBranchHandler(db);

        await Assert.ThrowsAsync<Ebi.Alas.Api.Composition.Errors.NotFoundException>(() =>
            handler.HandleAsync("999", new UpdateBranchRequest("X", "NA"), CancellationToken.None));
    }
}
