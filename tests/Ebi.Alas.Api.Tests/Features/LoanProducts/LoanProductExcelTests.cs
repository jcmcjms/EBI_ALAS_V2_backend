using ClosedXML.Excel;
using Ebi.Alas.Api.Features.LoanProducts;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class LoanProductExcelTests
{
    private static byte[] BuildWorkbook(
        string[] headers,
        object?[][] rows)
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
    public void Parse_ReadsPolicyColumns()
    {
        var xlsx = BuildWorkbook(
            LoanProductExcel.ImportHeaders,
            [["A16", "Salary Loan", 10000, 250000, 30, 365, 500, 200, 350, 0.12, 0.06, "DIM", "Yes", "No"]]);

        using var stream = new MemoryStream(xlsx);
        var rows = LoanProductExcel.Parse(stream);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("A16", row.Code);
        Assert.Equal("Salary Loan", row.Description);
        Assert.Equal(10000m, row.MinAmount);
        Assert.Equal(250000m, row.MaxAmount);
        Assert.Equal(30, row.MinTermDays);
        Assert.Equal(365, row.MaxTermDays);
        Assert.Equal(500m, row.NotarialFee);
        Assert.Equal(200m, row.DocStampFee);
        Assert.Equal(350m, row.InsuranceFee);
        Assert.Equal(0.12m, row.AdvanceInterestRate);
        Assert.Equal(0.06m, row.ApplicationChargeRate);
        Assert.Equal("DIM", row.AmortizationMode);
        Assert.True(row.ChargeAdvanceInterest);
        Assert.False(row.IsRetired);
    }

    [Fact]
    public void Parse_ThrowsWhenCodeHeaderMissing()
    {
        var xlsx = BuildWorkbook(["Description"], [["Salary Loan"]]);
        using var stream = new MemoryStream(xlsx);

        Assert.Throws<FormatException>(() => LoanProductExcel.Parse(stream));
    }

    [Fact]
    public void BuildTemplate_IncludesRequiredHeaders()
    {
        var bytes = LoanProductExcel.BuildTemplate();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();

        Assert.Equal("Code *", sheet.Cell(1, 1).GetString());
        Assert.Equal("Description *", sheet.Cell(1, 2).GetString());
        Assert.Equal("Min Amount *", sheet.Cell(1, 3).GetString());
    }

    [Fact]
    public void BuildExport_WritesProductRows()
    {
        var products = new List<LoanProductResponse>
        {
            new(
                "A16",
                "Salary Loan",
                10000m,
                250000m,
                30,
                365,
                500m,
                200m,
                350m,
                0.12m,
                0.06m,
                "DIM",
                true,
                false,
                DateTimeOffset.UtcNow),
        };

        var bytes = LoanProductExcel.BuildExport(products);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();

        Assert.Equal("A16", sheet.Cell(2, 1).GetString());
        Assert.Equal("Salary Loan", sheet.Cell(2, 2).GetString());
    }
}
