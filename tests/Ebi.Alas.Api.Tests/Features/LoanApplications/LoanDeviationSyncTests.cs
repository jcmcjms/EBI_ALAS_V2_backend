using System.Text.Json;
using Ebi.Alas.Api.Features.Deviations;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Notifications;
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
    public async Task SyncFromForm_HydratesRowsFromFormJsonOnly()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-DEV3", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);
        loan.SetFormSnapshot("""
            {
              "loans": [{
                "deviations": {
                  "deviationDetails": ["Lacking CIBI", "Lacking latest payslip"],
                  "deviationJustifications": { "Lacking CIBI": "Requested from bureau" }
                }
              }]
            }
            """);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        Assert.Empty(await db.LoanDeviations.Where(d => d.LoanApplicationId == loan.Id).ToListAsync());

        await LoanDeviationSync.SyncFromFormAsync(db, loan, loan.FormJson, CancellationToken.None);

        var rows = await db.LoanDeviations
            .Where(d => d.LoanApplicationId == loan.Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        var justifications = LoanDeviationSync.ExtractJustifications(loan.FormJson);
        Assert.Equal("Requested from bureau", justifications["Lacking CIBI"]);
    }

    private sealed class CapturingNotifier : IRealtimeNotifier
    {
        public List<(Guid UserId, string Title, string? Link)> Sent { get; } = [];

        public Task NotifyUserAsync(
            Guid notificationId,
            Guid userId,
            string title,
            string body,
            string? link,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            Sent.Add((userId, title, link));
            return Task.CompletedTask;
        }

        public Task NotifyDashboardUpdatedAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task NotifyLoanAssignedAsync(
            Guid loanId,
            Guid userId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task CreateDeviationRemark_NotifiesParentAuthorAndEncoder()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var evaluator = User.Create("ben", "reyes", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Evaluator, now, "CI");
        var loan = LoanApplication.Create(
            "LAM-DEVN", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        var deviation = LoanDeviation.Create(loan.Id, "6", "Lacking CIBI", 1, now);
        db.Users.AddRange(encoder, evaluator);
        db.LoanApplications.Add(loan);
        db.LoanDeviations.Add(deviation);
        var parent = DeviationRemark.Create(deviation.Id, null, "Need CIBI", evaluator.Id, now);
        db.DeviationRemarks.Add(parent);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var service = new DeviationRemarkService(db, notifier, TimeProvider.System);

        await service.CreateAsync(
            loan,
            deviation,
            "Will upload tomorrow",
            parent.Id,
            encoder.Id,
            CancellationToken.None);

        Assert.Contains(notifier.Sent, s =>
            s.UserId == evaluator.Id
            && s.Title == "New deviation remark"
            && s.Link != null && s.Link.StartsWith($"/loans/approval/{loan.Id}#deviations:", StringComparison.Ordinal));
        Assert.DoesNotContain(notifier.Sent, s => s.UserId == encoder.Id);
    }
}
