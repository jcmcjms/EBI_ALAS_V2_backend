using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class ProductDocumentRequirementSeederTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"req-seed-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task SeedAsync_InsertsCatalog_WhenEmpty()
    {
        await using var db = Db();
        var seeder = new ProductDocumentRequirementSeeder(db);

        var inserted = await seeder.SeedAsync(CancellationToken.None);

        Assert.True(inserted);
        Assert.True(await db.LoanProductDocumentRequirements.AnyAsync());
        Assert.Equal(
            ProductDocumentRequirementCatalog.Defaults.Count,
            await db.LoanProductDocumentRequirements.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await using var db = Db();
        var seeder = new ProductDocumentRequirementSeeder(db);
        await seeder.SeedAsync(CancellationToken.None);

        var insertedAgain = await seeder.SeedAsync(CancellationToken.None);

        Assert.False(insertedAgain);
        Assert.Equal(
            ProductDocumentRequirementCatalog.Defaults.Count,
            await db.LoanProductDocumentRequirements.CountAsync());
    }
}
