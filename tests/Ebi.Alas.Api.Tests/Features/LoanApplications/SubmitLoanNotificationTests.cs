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

public sealed class SubmitLoanNotificationTests
{
    private sealed record UserNotify(Guid UserId, string Title, string? Link);

    private sealed class CapturingNotifier : IRealtimeNotifier
    {
        public List<UserNotify> Sent { get; } = [];
        public int DashboardUpdates { get; private set; }

        public Task NotifyUserAsync(
            Guid notificationId,
            Guid userId,
            string title,
            string body,
            string? link,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            Sent.Add(new UserNotify(userId, title, link));
            return Task.CompletedTask;
        }

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
            .UseInMemoryDatabase($"submit-notify-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static IWorkflowConfiguration Config(bool requireRecommendation = true)
    {
        var store = new FakeSettingsStore(
            new SettingSnapshot(requireRecommendation, DateTime.UtcNow, "Admin"));
        return new WorkflowConfiguration(
            Options.Create(new WorkflowOptions()).ToMonitor(),
            store);
    }

    private static SubmitLoanApplicationsRequest Request(string branchId) => new(
        "G-1",
        "Juan Dela Cruz",
        branchId,
        LoanType.New,
        [new SubmitLoanLine("A16", "A16 - Salary Loan", 1000m, 30, 0.05m)],
        System.Text.Json.JsonDocument.Parse(
            """{"client":{"cisId":"0880530879","address":"123 Rizal St"},"loans":[]}""").RootElement.Clone());

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }

    [Fact]
    public async Task Submit_NotifiesNextDeskWithApprovalLink_AndEncoderConfirmation()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var recommender = User.Create("ben", "reyes", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Recommender, now, "AO");
        db.Users.AddRange(encoder, recommender);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var handler = new SubmitLoanHandler(
            db,
            new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
            new WorkflowQueueService(db, TimeProvider.System),
            Config(requireRecommendation: true),
            new ApplicationNumberGenerator(db),
            new ApplicationArrivalNotifier(db, notifier, TimeProvider.System),
            new EmptyChecklistReader(),
            new FakeWebLoanReader(),
            notifier,
            TimeProvider.System);

        var result = await handler.HandleAsync(
            Request("BR-A"),
            new CallerContext(encoder.Id, UserRole.Encoder, "BR-A"),
            CancellationToken.None);

        var loanId = result.Loans[0].Id;
        Assert.Contains(notifier.Sent, s =>
            s.UserId == recommender.Id
            && s.Title.Contains("ready for recommendation")
            && s.Link == $"/loans/approval/{loanId}");
        Assert.Contains(notifier.Sent, s =>
            s.UserId == encoder.Id
            && s.Title.Contains("Application submitted")
            && s.Link == $"/loans/approval/{loanId}");
        Assert.Equal(1, notifier.DashboardUpdates);
    }

    [Fact]
    public async Task Submit_WhenRecommendationSkipped_NotifiesEvaluatorDesk()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var evaluator = User.Create("bea", "reyes", "Bea", "Reyes", "", "bea@example.com", "BR-A", UserRole.Evaluator, now, "CI");
        db.Users.AddRange(encoder, evaluator);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var handler = new SubmitLoanHandler(
            db,
            new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
            new WorkflowQueueService(db, TimeProvider.System),
            Config(requireRecommendation: false),
            new ApplicationNumberGenerator(db),
            new ApplicationArrivalNotifier(db, notifier, TimeProvider.System),
            new EmptyChecklistReader(),
            new FakeWebLoanReader(),
            notifier,
            TimeProvider.System);

        var result = await handler.HandleAsync(
            Request("BR-A"),
            new CallerContext(encoder.Id, UserRole.Encoder, "BR-A"),
            CancellationToken.None);

        Assert.Contains(notifier.Sent, s =>
            s.UserId == evaluator.Id
            && s.Title.Contains("ready for evaluation")
            && s.Link == $"/loans/approval/{result.Loans[0].Id}");
    }
}
