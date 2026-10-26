using System.Reflection;
using Ebi.Alas.Api.Features.Users.ImportUsers;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class ImportUsersJobTitleTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"import-job-{Guid.NewGuid():N}")
            .Options);

    private static byte[] BuildXlsx()
    {
        using var ms = new MemoryStream();
        using (var workbook = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Users");
            sheet.Cell(1, 1).Value = "Username";
            sheet.Cell(1, 2).Value = "First Name";
            sheet.Cell(1, 3).Value = "Last Name";
            sheet.Cell(1, 4).Value = "Branch Code";
            sheet.Cell(1, 5).Value = "Role";
            sheet.Cell(1, 6).Value = "Job Title";
            sheet.Cell(2, 1).Value = "jdoe";
            sheet.Cell(2, 2).Value = "Juan";
            sheet.Cell(2, 3).Value = "Cruz";
            sheet.Cell(2, 4).Value = "011";
            sheet.Cell(2, 5).Value = "Evaluator";
            sheet.Cell(2, 6).Value = "Credit Checker";
            workbook.SaveAs(ms);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Import_SavesJobTitleFromTemplate()
    {
        await using var db = Db();
        var handler = new ImportUsersHandler(
            db,
            new Ebi.Alas.Api.Features.Auth.PasswordHasher(),
            TimeProvider.System,
            new Ebi.Alas.Api.Features.Approvals.ApprovalAuthorityAssigner(db));

        await handler.ImportAsync(new MemoryStream(BuildXlsx()), CancellationToken.None);

        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("Credit Checker", user.JobTitle);
    }
}
