using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Features.LoanProducts.DocumentRequirements;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class DocumentRequirementHandlersTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"req-api-{Guid.NewGuid():N}")
            .Options);

    private static async Task SeedProductAsync(AlasDbContext db, string code)
    {
        db.LoanProducts.Add(LoanProduct.Create(code, code, 0.01m, 1, 30, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Replace_ReplacesFullList()
    {
        await using var db = Db();
        await SeedProductAsync(db, "A16");
        db.LoanProductDocumentRequirements.Add(
            LoanProductDocumentRequirement.Create("A16", "OLD", "OLD"));
        await db.SaveChangesAsync();
        var handler = new ReplaceDocumentRequirementsHandler(db);

        var result = await handler.HandleAsync(
            "A16",
            new ReplaceDocumentRequirementsRequest(
                [new DocumentRequirementItemDto("A1004", "A1004"),
                 new DocumentRequirementItemDto("PIC02", "Photo ID")]),
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Photo ID", result.Single(i => i.Code == "PIC02").Description);
        Assert.DoesNotContain(db.LoanProductDocumentRequirements, r => r.Code == "OLD");
    }

    [Fact]
    public async Task Replace_RejectsDuplicateCodes()
    {
        await using var db = Db();
        await SeedProductAsync(db, "A16");
        var handler = new ReplaceDocumentRequirementsHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            "A16",
            new ReplaceDocumentRequirementsRequest(
                [new DocumentRequirementItemDto("A1004", "A"),
                 new DocumentRequirementItemDto("A1004", "B")]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenProductMissing()
    {
        await using var db = Db();
        var handler = new GetDocumentRequirementsHandler(db);

        await Assert.ThrowsAsync<Ebi.Alas.Api.Composition.Errors.NotFoundException>(
            () => handler.HandleAsync("NOPE", CancellationToken.None));
    }

    [Fact]
    public async Task Get_ReturnsOrderedList()
    {
        await using var db = Db();
        await SeedProductAsync(db, "A16");
        db.LoanProductDocumentRequirements.Add(
            LoanProductDocumentRequirement.Create("A16", "PIC02", "Photo ID"));
        db.LoanProductDocumentRequirements.Add(
            LoanProductDocumentRequirement.Create("A16", "A1004", "A1004"));
        await db.SaveChangesAsync();
        var handler = new GetDocumentRequirementsHandler(db);

        var items = await handler.HandleAsync("A16", CancellationToken.None);

        Assert.Equal(["A1004", "PIC02"], items.Select(i => i.Code));
    }
}
