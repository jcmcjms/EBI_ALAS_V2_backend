using ClosedXML.Excel;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.ImportUsers;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users.ImportUsers;

public sealed class ImportUsersHandlerTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"import-users-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static ImportUsersHandler Handler(AlasDbContext db) =>
        new(db, new PasswordHasher(), new FixedTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)));

    private static byte[] BuildWorkbook(params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Users");
        string[] headers = ["Username", "First Name", "Middle Name", "Last Name", "Email", "Branch Code", "Role"];
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
    public async Task ImportAsync_ValidRow_CreatesUserWithGeneratedPassword()
    {
        await using var db = Db();
        var result = await Handler(db).ImportAsync(
            new MemoryStream(BuildWorkbook(["jdoe", "Juan", "Dela", "Cruz", "jdoe@x.com", "011", "Encoder"])),
            CancellationToken.None);

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.SuccessfulImports);
        Assert.Equal(0, result.FailedImports);
        Assert.Equal(["jdoe"], result.CreatedUsernames);
        var cred = Assert.Single(result.CreatedCredentials);
        Assert.Equal("jdoe", cred.Username);
        Assert.True(cred.TemporaryPassword.Length >= 12);

        var user = await db.Users.SingleAsync(u => u.UserName == "jdoe");
        Assert.True(user.MustChangePassword);
        Assert.Equal("Juan Dela Cruz", user.FullName);
    }

    [Fact]
    public async Task ImportAsync_InvalidRow_SkipsAndReportsError()
    {
        await using var db = Db();
        var result = await Handler(db).ImportAsync(
            new MemoryStream(BuildWorkbook(["", "Juan", null, "Cruz", null, "011", "Nope"])),
            CancellationToken.None);

        Assert.Equal(0, result.SuccessfulImports);
        Assert.Equal(1, result.FailedImports);
        Assert.Contains(result.Errors, e => e.Field == "Username");
        Assert.Contains(result.Errors, e => e.Field == "Role");
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_DuplicateUsername_SkipsRow()
    {
        await using var db = Db();
        db.Users.Add(User.Create("jdoe", "hash", "Existing", "e@x.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var result = await Handler(db).ImportAsync(
            new MemoryStream(BuildWorkbook(["jdoe", "Juan", null, "Cruz", null, "011", "Encoder"])),
            CancellationToken.None);

        Assert.Equal(0, result.SuccessfulImports);
        Assert.Contains(result.Errors, e => e.Field == "Username" && e.Error.Contains("exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_EmptyRows_AreIgnored()
    {
        await using var db = Db();
        var result = await Handler(db).ImportAsync(
            new MemoryStream(BuildWorkbook(
                ["jdoe", "Juan", null, "Cruz", null, "011", "Encoder"],
                [null, null, null, null, null, null, null],
                ["asmith", "Ana", null, "Smith", null, "011", "Approver"])),
            CancellationToken.None);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.SuccessfulImports);
    }
}
