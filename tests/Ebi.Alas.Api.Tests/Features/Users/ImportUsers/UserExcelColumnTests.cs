using ClosedXML.Excel;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.ImportUsers;

namespace Ebi.Alas.Api.Tests.Features.Users.ImportUsers;

public sealed class UserExcelColumnTests
{
    private static byte[] Workbook(string[] headers, params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Template");
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] is { } v)
                {
                    ws.Cell(r + 2, c + 1).Value = v.ToString();
                }
            }
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static readonly string[] LegacyTemplateHeaders =
    [
        "Username *", "First Name *", "Middle Name", "Last Name *",
        "Branch Code *", "Role *", "Job Title", "Covered Branches",
        "Email", "Phone", "MustChangePassword", "Password"
    ];

    [Fact]
    public void Parse_LegacyTemplateLayout_ReadsRoleFromColumnSix()
    {
        var bytes = Workbook(
            LegacyTemplateHeaders,
            ["encoder0", "encoder", null, "00", "000", "Encoder", "Account Officer", null, "a@x.com", null, 0, "@Temp123!"]);

        var rows = UserExcel.Parse(new MemoryStream(bytes));
        var row = Assert.Single(rows);

        Assert.Equal("encoder0", row.Username);
        Assert.Equal("000", row.BranchCode);
        Assert.Equal("Encoder", row.Role);
        Assert.Equal("Account Officer", row.JobTitle);
        Assert.Equal("a@x.com", row.Email);
        Assert.False(row.MustChangePassword);
        Assert.Equal("@Temp123!", row.Password);
    }

    [Fact]
    public void Parse_SevenColumnTemplate_ReadsEmailBeforeBranch()
    {
        var bytes = Workbook(
            UserExcel.Headers,
            ["jdoe", "Juan", "Dela", "Cruz", "j@x.com", "011", "Admin"]);

        var rows = UserExcel.Parse(new MemoryStream(bytes));
        var row = Assert.Single(rows);

        Assert.Equal("011", row.BranchCode);
        Assert.Equal("Admin", row.Role);
        Assert.Equal("j@x.com", row.Email);
    }

    [Fact]
    public void TryParseRole_MapsJobTitleStyleNamesUsedInImportSheets()
    {
        Assert.True(ImportUsersHandler.TryParseRole("Account Officer", out var ao));
        Assert.Equal(UserRole.Encoder, ao);
        Assert.True(ImportUsersHandler.TryParseRole("Credit Checker", out var cc));
        Assert.Equal(UserRole.Evaluator, cc);
        Assert.True(ImportUsersHandler.TryParseRole("BranchHead", out var bh));
        Assert.Equal(UserRole.Recommender, bh);
    }
}
