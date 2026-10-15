using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Documents;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class DocumentFlagDashboardNotifyTests
{
    private sealed class CapturingNotifier : IRealtimeNotifier
    {
        public int DashboardUpdates { get; private set; }

        public Task NotifyUserAsync(
            Guid notificationId,
            Guid userId,
            string title,
            string body,
            string? link,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifyDashboardUpdatedAsync(CancellationToken cancellationToken)
        {
            DashboardUpdates++;
            return Task.CompletedTask;
        }

        public Task NotifyLoanAssignedAsync(
            Guid loanId,
            Guid userId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"doc-flag-dash-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication Loan(string branchId, Guid createdBy)
    {
        var now = DateTimeOffset.UtcNow;
        return LoanApplication.Create(
            $"LAM-{Guid.NewGuid():N}"[..32],
            "G-1",
            "Client",
            branchId,
            LoanType.New,
            1000m,
            30,
            0.05m,
            50m,
            10m,
            940m,
            createdBy,
            now);
    }

    [Fact]
    public async Task ClearDocumentFlag_EmitsDashboardUpdated()
    {
        await using var db = Db();
        var encoder = Guid.NewGuid();
        var flagger = Guid.NewGuid();
        var loan = Loan("BR-A", encoder);
        loan.FlagDocuments(1, "missing docs", flagger, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var handler = new DocumentFlagHandler(db, notifier, TimeProvider.System);

        await handler.ClearAsync(
            loan.Id,
            new CallerContext(encoder, UserRole.Encoder, "BR-A"),
            CancellationToken.None);

        Assert.Equal(1, notifier.DashboardUpdates);
    }
}
