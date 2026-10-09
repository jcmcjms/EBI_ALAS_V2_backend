using ClosedXML.Excel;
using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class ImportLoanProductsHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-product-import-{Guid.NewGuid():N}")
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
    public async Task ImportAsync_CreatesAndUpdatesProducts()
    {
        await using var db = Db();
        db.LoanProducts.Add(LoanProduct.Create("A16", "Old Name", 0.02m, 30, 365, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var xlsx = Workbook(
            LoanProductExcel.ImportHeaders,
            [
                ["A16", "Updated Name", 5000, 50000, 15, 180, 100, 50, 25, 0.08, 0.05, "DIM", "No", "No"],
                ["C99", "New Product", 10000, 100000, 30, 365, 200, 100, 50, 0.10, 0.06, "MIC", "Yes", "No"],
            ]);

        var handler = new ImportLoanProductsHandler(db);
        await using var stream = new MemoryStream(xlsx);
        var result = await handler.ImportAsync(stream, CancellationToken.None);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);

        var updated = await db.LoanProducts.AsNoTracking().SingleAsync(p => p.Code == "A16");
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal(5000m, updated.MinAmount);

        var created = await db.LoanProducts.AsNoTracking().SingleAsync(p => p.Code == "C99");
        Assert.Equal("New Product", created.Name);
        Assert.True(created.ChargeAdvanceInterest);
    }

    [Fact]
    public async Task ImportAsync_ReportsRowValidationErrors()
    {
        await using var db = Db();
        var xlsx = Workbook(
            LoanProductExcel.ImportHeaders,
            [
                ["", "Missing Code", 1, 2, 1, 2, 0, 0, 0, 0, 0, "DIM", "No", "No"],
                ["C99", "Ok Product", 100, 200, 1, 2, 0, 0, 0, 0.01m, 0, "DIM", "No", "No"],
            ]);

        var handler = new ImportLoanProductsHandler(db);
        await using var stream = new MemoryStream(xlsx);
        var result = await handler.ImportAsync(stream, CancellationToken.None);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Failed);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.RowNumber);
        Assert.Equal("Code", error.Field);
    }
}
