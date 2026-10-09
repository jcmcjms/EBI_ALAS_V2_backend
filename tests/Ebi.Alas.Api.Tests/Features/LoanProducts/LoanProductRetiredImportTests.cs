using ClosedXML.Excel;
using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class LoanProductRetiredImportTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-product-retired-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static byte[] Workbook(string[] headers, object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Loan Products");
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] is { } value)
                {
                    sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(value);
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task ImportAsync_CreatesRetiredProductWhenFileSaysYes()
    {
        await using var db = Db();
        var xlsx = Workbook(
            LoanProductExcel.ImportHeaders,
            [
                ["R01", "Legacy Product", 1000, 5000, 30, 90, 0, 0, 0, 0, 0, "DIM", "No", "Yes"],
                ["A01", "Active Product", 1000, 5000, 30, 90, 0, 0, 0, 0, 0, "DIM", "No", "No"],
            ]);

        var handler = new ImportLoanProductsHandler(db);
        await using var stream = new MemoryStream(xlsx);
        var result = await handler.ImportAsync(stream, CancellationToken.None);

        Assert.Equal(0, result.Failed);
        Assert.False((await db.LoanProducts.SingleAsync(p => p.Code == "R01")).IsActive);
        Assert.True((await db.LoanProducts.SingleAsync(p => p.Code == "A01")).IsActive);

        var retired = (await db.LoanProducts.SingleAsync(p => p.Code == "R01")).ToResponse();
        Assert.True(retired.IsRetired);
        var active = (await db.LoanProducts.SingleAsync(p => p.Code == "A01")).ToResponse();
        Assert.False(active.IsRetired);
    }

    [Fact]
    public async Task ImportAsync_IgnoresIsRetiredOnUpdate()
    {
        await using var db = Db();
        var product = LoanProduct.Create("A01", "Active Product", 0.01m, 30, 90, DateTimeOffset.UtcNow);
        db.LoanProducts.Add(product);
        await db.SaveChangesAsync();

        var xlsx = Workbook(
            LoanProductExcel.ImportHeaders,
            [
                ["A01", "Active Product", 1000, 5000, 30, 90, 0, 0, 0, 0, 0, "DIM", "No", "Yes"],
            ]);

        var handler = new ImportLoanProductsHandler(db);
        await using var stream = new MemoryStream(xlsx);
        await handler.ImportAsync(stream, CancellationToken.None);

        // Retirement is owned by webloan sync, not import updates.
        var reloaded = await db.LoanProducts.AsNoTracking().SingleAsync(p => p.Code == "A01");
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task ImportAsync_BlankIsRetiredKeepsCurrentStateOnUpdate()
    {
        await using var db = Db();
        var product = LoanProduct.Create("A01", "Active Product", 0.01m, 30, 90, DateTimeOffset.UtcNow);
        product.Deactivate();
        db.LoanProducts.Add(product);
        await db.SaveChangesAsync();

        var xlsx = Workbook(
            LoanProductExcel.ImportHeaders,
            [
                ["A01", "Active Product", 1000, 5000, 30, 90, 0, 0, 0, 0, 0, "DIM", "No", null],
            ]);

        var handler = new ImportLoanProductsHandler(db);
        await using var stream = new MemoryStream(xlsx);
        await handler.ImportAsync(stream, CancellationToken.None);

        var reloaded = await db.LoanProducts.AsNoTracking().SingleAsync(p => p.Code == "A01");
        Assert.False(reloaded.IsActive);
    }
}
