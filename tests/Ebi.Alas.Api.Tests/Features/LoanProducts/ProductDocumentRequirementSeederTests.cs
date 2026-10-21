using Ebi.Alas.Api.Features.LoanApplications.Domain;
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
    public void Catalog_MatchesExcelMatrix()
    {
        var defaults = ProductDocumentRequirementCatalog.Defaults;

        // A16 New(6)+Reloan(3) + A17 New(6)+Reloan(3) + C21 New(7)+Reloan(4) + C02/C23/C35 New(3 each)
        Assert.Equal(6 + 3 + 6 + 3 + 7 + 4 + 3 + 3 + 3, defaults.Count);

        AssertCodes(defaults, "A16", LoanType.New, "A1004", "CCR38", "A2016", "A2008", "PIC03", "A2020");
        AssertCodes(defaults, "A17", LoanType.New, "A1004", "CCR38", "A2016", "A2008", "PIC03", "A2020");
        AssertCodes(defaults, "A16", LoanType.Reloan, "CCR38", "A2016", "A2008");
        AssertCodes(defaults, "A17", LoanType.Reloan, "CCR38", "A2016", "A2008");
        AssertCodes(defaults, "C21", LoanType.New, "A1004", "CCR38", "A2008", "PIC03", "A2020", "A2023", "A2027");
        AssertCodes(defaults, "C21", LoanType.Reloan, "A1004", "CCR38", "A2008", "A2027");
        AssertCodes(defaults, "C02", LoanType.New, "A1004", "CCR38", "A2008");
        AssertCodes(defaults, "C23", LoanType.New, "A1004", "CCR38", "A2008");
        AssertCodes(defaults, "C35", LoanType.New, "A1004", "CCR38", "A2008");
        AssertCodes(defaults, "C02", LoanType.Reloan);
        AssertCodes(defaults, "C23", LoanType.Reloan);
        AssertCodes(defaults, "C35", LoanType.Reloan);
    }

    [Fact]
    public async Task SeedAsync_InsertsCatalog_WhenEmpty()
    {
        await using var db = Db();
        var seeder = new ProductDocumentRequirementSeeder(db);

        var inserted = await seeder.SeedAsync(CancellationToken.None);

        Assert.True(inserted);
        Assert.Equal(
            ProductDocumentRequirementCatalog.Defaults.Count,
            await db.LoanProductDocumentRequirements.CountAsync());
        Assert.True(await db.LoanProductDocumentRequirements.AnyAsync(
            r => r.ProductCode == "C21" && r.LoanType == LoanType.Reloan && r.Code == "A2027"));
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

    private static void AssertCodes(
        IReadOnlyList<(string ProductCode, LoanType LoanType, string Code, string Description)> defaults,
        string productCode,
        LoanType loanType,
        params string[] expectedCodes)
    {
        var actual = defaults
            .Where(d => d.ProductCode == productCode && d.LoanType == loanType)
            .Select(d => d.Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedCodes.OrderBy(c => c, StringComparer.Ordinal).ToArray(), actual);
    }
}
