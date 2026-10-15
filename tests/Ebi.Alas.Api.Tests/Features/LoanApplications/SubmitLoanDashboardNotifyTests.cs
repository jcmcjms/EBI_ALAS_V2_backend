using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class SubmitLoanDashboardNotifyTests
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
            .UseInMemoryDatabase($"submit-dash-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static SubmitLoanApplicationsRequest Request(string branchId) => new(
        "G-1",
        "Client",
        branchId,
        LoanType.New,
        [new SubmitLoanLine("A16", "A16 - Salary Loan", 1000m, 30, 0.05m)]);

    private static IWorkflowConfiguration Config()
    {
        var store = new FakeSettingsStore(
            new SettingSnapshot(true, DateTime.UtcNow, "Admin"));
        return new WorkflowConfiguration(
            Options.Create(new WorkflowOptions()).ToMonitor(),
            store);
    }

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }

    [Fact]
    public async Task Submit_EmitsDashboardUpdated()
    {
        await using var db = Db();
        var notifier = new CapturingNotifier();
        var handler = new SubmitLoanHandler(
            db,
            new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
            new WorkflowQueueService(db, TimeProvider.System),
            Config(),
            new ApplicationNumberGenerator(db),
            new ApplicationArrivalNotifier(db, null, TimeProvider.System),
            notifier,
            TimeProvider.System);

        await handler.HandleAsync(
            Request("BR-A"),
            new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A"),
            CancellationToken.None);

        Assert.Equal(1, notifier.DashboardUpdates);
    }
}
