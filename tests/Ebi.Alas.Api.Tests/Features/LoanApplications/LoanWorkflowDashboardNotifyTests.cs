using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanWorkflowDashboardNotifyTests
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
            .UseInMemoryDatabase($"dash-notify-{Guid.NewGuid():N}")
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

    private static (LoanWorkflowService Service, CapturingNotifier Notifier) Service(AlasDbContext db)
    {
        var notifier = new CapturingNotifier();
        var service = new LoanWorkflowService(
            db,
            new WorkflowQueueService(db, TimeProvider.System),
            notifier,
            new ApplicationArrivalNotifier(db, null, TimeProvider.System),
            TimeProvider.System);
        return (service, notifier);
    }

    [Fact]
    public async Task Recommend_EmitsDashboardUpdated()
    {
        await using var db = Db();
        var loan = Loan("BR-A", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var (service, notifier) = Service(db);
        await service.RecommendAsync(
            loan.Id,
            new CallerContext(Guid.NewGuid(), UserRole.Recommender, "BR-A"),
            CancellationToken.None);

        Assert.Equal(1, notifier.DashboardUpdates);
    }

    [Fact]
    public async Task Cancel_EmitsDashboardUpdated()
    {
        await using var db = Db();
        var creator = Guid.NewGuid();
        var loan = Loan("BR-A", creator);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var (service, notifier) = Service(db);
        await service.CancelAsync(
            loan.Id,
            new CallerContext(creator, UserRole.Encoder, "BR-A"),
            CancellationToken.None);

        Assert.Equal(1, notifier.DashboardUpdates);
    }
}
