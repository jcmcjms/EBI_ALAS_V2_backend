using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Branches;

public sealed class BranchVisibilityTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"branch-vis-{Guid.NewGuid():N}")
            .Options);

    [Theory]
    [InlineData("AreaHead", true)]
    [InlineData("AreaHeadLevelH", true)]
    [InlineData("areahead", true)]
    [InlineData("BranchHead", false)]
    [InlineData("OICLevel1", false)]
    [InlineData(null, false)]
    public void IsAreaHead_MatchesJobTitle(string? jobTitle, bool expected)
        => Assert.Equal(expected, BranchVisibility.IsAreaHead(jobTitle));

    [Fact]
    public async Task Encoder_IsHomeBranchOnly()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("001", "Main", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("002", "Satellite", "A1", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var ids = await BranchVisibility.GetReadableBranchIdsAsync(
            db, Guid.NewGuid(), UserRole.Encoder, "001", CancellationToken.None);

        Assert.Equal(["001"], ids);
    }

    [Fact]
    public async Task AreaHead_SeesAllBranchesInArea()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("001", "Main", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("002", "Satellite", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("003", "Other", "B9", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var ids = await BranchVisibility.GetReadableBranchIdsAsync(
            db, Guid.NewGuid(), UserRole.Approver, "001", CancellationToken.None, jobTitle: "AreaHead");

        Assert.Contains("001", ids);
        Assert.Contains("002", ids);
        Assert.DoesNotContain("003", ids);
    }

    [Fact]
    public async Task Evaluator_SeesAllBranches()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("001", "Main", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("002", "Satellite", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("003", "Other", "B9", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var ids = await BranchVisibility.GetReadableBranchIdsAsync(
            db, Guid.NewGuid(), UserRole.Evaluator, "001", CancellationToken.None);

        Assert.Equal(3, ids.Count);
        Assert.Contains("001", ids);
        Assert.Contains("002", ids);
        Assert.Contains("003", ids);
    }

    [Fact]
    public async Task Evaluator_IsHomeBranch_SeesAllAnyway()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("001", "Main", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("009", "Remote", "Z9", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var ids = await BranchVisibility.GetReadableBranchIdsAsync(
            db, Guid.NewGuid(), UserRole.Evaluator, "001", CancellationToken.None);

        Assert.Contains("009", ids);
    }
}
