using System.Text.Json;
using Ebi.Alas.Api.Features.Deviations;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanDeviationSyncTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"dev-sync-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task SyncFromForm_CreatesLoanDeviationRowsForDeclaredReasons()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-DEV", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);
        var formJson = JsonSerializer.Serialize(new
        {
            loans = new[]
            {
                new
                {
                    deviations = new
                    {
                        hasDeviations = true,
                        deviationDetails = new[] { "Lacking CIBI", "Lacking CIBI" },
                    },
                },
            },
        });
        loan.SetFormSnapshot(formJson);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        await LoanDeviationSync.SyncFromFormAsync(db, loan, formJson, CancellationToken.None);

        var rows = await db.LoanDeviations
            .Where(d => d.LoanApplicationId == loan.Id)
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("Lacking CIBI", row.Description);
        Assert.Equal("6", row.Code);
    }

    [Fact]
    public async Task GetLoanDeviations_IncludesSyncedRowsAndRemarks()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var author = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var loan = LoanApplication.Create(
            "LAM-DEV2", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, author.Id, now);
        loan.SetFormSnapshot("""{"loans":[{"deviations":{"deviationDetails":["Lacking CIBI"]}}]}""");
        db.Users.Add(author);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        await LoanDeviationSync.SyncFromFormAsync(db, loan, loan.FormJson, CancellationToken.None);

        var deviation = await db.LoanDeviations.SingleAsync(d => d.LoanApplicationId == loan.Id);
        db.DeviationRemarks.Add(DeviationRemark.Create(
            deviation.Id, null, "CIBI request filed", author.Id, now));
        await db.SaveChangesAsync();

        var remarks = await db.DeviationRemarks
            .Where(r => r.LoanDeviationId == deviation.Id)
            .ToListAsync();
        Assert.Single(remarks);
        Assert.Equal("CIBI request filed", remarks[0].Body);
    }
}
