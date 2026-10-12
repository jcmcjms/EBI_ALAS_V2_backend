using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class ListLoanProductsHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-product-list-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task SeedAsync(AlasDbContext db, int count)
    {
        for (var i = 0; i < count; i++)
        {
            db.LoanProducts.Add(LoanProduct.Create(
                $"P{i:D3}",
                $"Product {i:D3}",
                0.02m,
                30,
                365,
                DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_ReturnsOnlyOnePageOfResults()
    {
        await using var db = Db();
        await SeedAsync(db, 25);
        var handler = new ListLoanProductsHandler(db);

        var result = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 10 },
            maxPageSize: 100,
            defaultPageSize: 20,
            CancellationToken.None);

        Assert.Equal(25, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task HandleAsync_LastPage_ReturnsRemainingItems()
    {
        await using var db = Db();
        await SeedAsync(db, 25);
        var handler = new ListLoanProductsHandler(db);

        var result = await handler.HandleAsync(
            new PageRequest { Page = 3, PageSize = 10 },
            maxPageSize: 100,
            defaultPageSize: 20,
            CancellationToken.None);

        Assert.Equal(25, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_PageSizeIsCappedAtMaxPageSize()
    {
        await using var db = Db();
        await SeedAsync(db, 30);
        var handler = new ListLoanProductsHandler(db);

        var result = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 1000 },
            maxPageSize: 100,
            defaultPageSize: 20,
            CancellationToken.None);

        Assert.Equal(100, result.PageSize);
        Assert.Equal(30, result.Items.Count);
    }
}
