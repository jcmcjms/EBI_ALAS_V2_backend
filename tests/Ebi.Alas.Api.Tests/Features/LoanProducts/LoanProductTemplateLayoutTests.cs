using ClosedXML.Excel;
using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

/// <summary>
/// Contract tests against the production import layout used by
/// Testing Excel/loan-product-import-template.xlsx.
/// </summary>
public sealed class LoanProductTemplateLayoutTests
{
    /// <summary>Header row exactly as in the production template.</summary>
    public static readonly string[] ProductionHeaders =
    [
        "Code *", "Description *", "Min Amount *", "Max Amount *",
        "Min Term Days *", "Max Term Days *", "Notarial Fee",
        "Doc Stamp Fee", "Insurance Fee", "Advance Interest Rate *",
        "Application Charge Rate *", "Amortization Mode *",
        "Charge Advance Interest *", "Is Retired"
    ];

    private static byte[] SparseTemplateWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Template");
        for (var i = 0; i < ProductionHeaders.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = ProductionHeaders[i];
        }

        // Production sample rows omit optional rate/mode columns (J–M).
        object?[][] rows =
        [
            ["A06", "APDS DIM_RSL", 10000, 500000, 180, 2555, 500, 0, 0, null, null, null, null, "No"],
            ["A16", "APDS-RPSU 1-7YR_DIM", 8000, 1200000, 360, 2617, 500, 0, 0, null, null, null, null, "No"],
        ];
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
    public void Parse_AcceptsAsteriskHeadersAndSparseOptionalColumns()
    {
        using var stream = new MemoryStream(SparseTemplateWorkbook());
        var rows = LoanProductExcel.Parse(stream);

        Assert.Equal(2, rows.Count);
        Assert.Equal("A06", rows[0].Code);
        Assert.Equal("APDS DIM_RSL", rows[0].Description);
        Assert.Equal(10000m, rows[0].MinAmount);
        Assert.Null(rows[0].AdvanceInterestRate);
        Assert.Null(rows[0].AmortizationMode);
        Assert.Null(rows[0].ChargeAdvanceInterest);
        Assert.False(rows[0].IsRetired);
    }

    [Fact]
    public void BuildTemplate_UsesProductionHeaderLabels()
    {
        var bytes = LoanProductExcel.BuildTemplate();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Template");

        for (var i = 0; i < ProductionHeaders.Length; i++)
        {
            Assert.Equal(ProductionHeaders[i], sheet.Cell(1, i + 1).GetString());
        }
    }

    [Fact]
    public void BuildTemplate_IncludesInstructionsSheet()
    {
        var bytes = LoanProductExcel.BuildTemplate();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.Contains(workbook.Worksheets, w => w.Name == "Instructions");
    }

    [Fact]
    public async Task ImportAsync_CreatesProductsFromSparseTemplateRows()
    {
        await using var db = new AlasDbContext(
            new DbContextOptionsBuilder<AlasDbContext>()
                .UseInMemoryDatabase($"loan-product-template-{Guid.NewGuid():N}")
                .Options);

        var handler = new ImportLoanProductsHandler(db);
        await using var stream = new MemoryStream(SparseTemplateWorkbook());
        var result = await handler.ImportAsync(stream, CancellationToken.None);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);

        var product = await db.LoanProducts.AsNoTracking().SingleAsync(p => p.Code == "A06");
        Assert.Equal(0m, product.AdvanceInterestRate);
        Assert.Equal("DIM", product.AmortizationMode);
        Assert.False(product.ChargeAdvanceInterest);
    }
}
