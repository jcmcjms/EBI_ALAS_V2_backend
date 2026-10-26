using ClosedXML.Excel;
using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.ImportUsers;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users.ImportUsers;

public sealed class ImportUsersAuthorityAssignmentTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    }

    private static AlasDbContext Db() =>
        new(new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"import-auth-{Guid.NewGuid():N}")
            .Options);

    private static ImportUsersHandler Handler(AlasDbContext db) =>
        new(db, new PasswordHasher(), new FixedTimeProvider(), new ApprovalAuthorityAssigner(db));

    private static byte[] Workbook(params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Users");
        string[] headers =
        [
            "Username", "First Name", "Middle Name", "Last Name", "Email",
            "Branch Code", "Role", "Job Title",
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
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

    [Fact]
    public async Task ImportAsync_AreaHeadApprover_AssignedToAreaHeadAuthority()
    {
        await using var db = Db();
        await Handler(db).ImportAsync(
            new MemoryStream(Workbook(
                ["ahead", "Ana", null, "Cruz", null, "011", "Approver", "Area Head"])),
            CancellationToken.None);

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "ahead");
        var authority = await db.ApprovalAuthorities.AsNoTracking().SingleAsync(a => a.Key == "area-head");
        var assignment = await db.ApprovalAuthorityUsers.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, assignment.UserId);
        Assert.Equal(authority.Id, assignment.ApprovalAuthorityId);
    }

    [Fact]
    public async Task ImportAsync_WithoutJobTitle_DoesNotAssign()
    {
        await using var db = Db();
        await Handler(db).ImportAsync(
            new MemoryStream(Workbook(
                ["enc", "Eva", null, "Santos", null, "011", "Encoder", null])),
            CancellationToken.None);

        Assert.Empty(await db.ApprovalAuthorityUsers.AsNoTracking().ToListAsync());
    }
}
