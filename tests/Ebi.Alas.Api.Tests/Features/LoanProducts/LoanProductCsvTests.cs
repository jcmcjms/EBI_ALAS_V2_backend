using Ebi.Alas.Api.Features.LoanProducts;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class LoanProductCsvTests
{
    private static string CsvWithRows(int count)
    {
        var lines = new List<string> { "code,name,rate,minterm,maxterm" };
        for (var i = 0; i < count; i++)
        {
            lines.Add($"P{i},Product {i},0.05,30,90");
        }

        return string.Join('\n', lines);
    }

    [Fact]
    public void Parse_ValidRows_ReturnsRows()
    {
        var rows = LoanProductCsv.Parse(CsvWithRows(3));
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Parse_MalformedRows_SkipsThem()
    {
        var csv = "code,name,rate,minterm,maxterm\nP1,Name,0.05,30,90\nbad-row\nP2,Name,not-a-number,30,90";
        var rows = LoanProductCsv.Parse(csv);
        Assert.Single(rows);
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => LoanProductCsv.Parse("  "));
    }
}
