using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Revise;
using Ebi.Alas.Api.Features.LoanComputation;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class ReviseLoanHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"revise-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication SeedForRevision(AlasDbContext db, string branchId = "BR-A")
    {
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-20261015-000001",
            "G-1",
            "Juan Dela Cruz",
            branchId,
            LoanType.New,
            1000m,
            30,
            0.05m,
            15m,
            50m,
            950m,
            Guid.NewGuid(),
            now,
            "A16",
            "A16 - Salary Loan");
        loan.TransitionTo(LoanStatus.ForRevision, now, loan.CreatedBy, "PushedBack");
        loan.SetFormSnapshot("""{"client":{"cisId":"CIS-1","firstName":"Juan"},"loans":[]}""");
        db.LoanApplications.Add(loan);
        db.SaveChanges();
        return loan;
    }

    [Fact]
    public void BumpRowVersion_ChangesToken()
    {
        var loan = SeedForRevision(Db());
        var before = Convert.ToBase64String(loan.RowVersion);
        loan.BumpRowVersion();
        var after = Convert.ToBase64String(loan.RowVersion);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ApplyRevision_DoesNotChangeIdentityFields()
    {
        var loan = SeedForRevision(Db());
        var lam = loan.LamId;
        var branch = loan.BranchId;
        var product = loan.ProductCode;
        loan.ApplyRevision(
            "New Name",
            2000m,
            45,
            0.06m,
            10m,
            5m,
            50m,
            "{}",
            DateTimeOffset.UtcNow);
        Assert.Equal(lam, loan.LamId);
        Assert.Equal(branch, loan.BranchId);
        Assert.Equal(product, loan.ProductCode);
    }

    private static IWorkflowConfiguration Config(bool requireRecommendation)
    {
        var store = new FakeSettingsStore(
            requireRecommendation
                ? new SettingSnapshot(true, DateTime.UtcNow, "Admin")
                : new SettingSnapshot(false, DateTime.UtcNow, "Admin"));
        return new WorkflowConfiguration(
            Options.Create(new WorkflowOptions()).ToMonitor(),
            store);
    }

    private static ReviseLoanHandler Handler(
        AlasDbContext db,
        bool requireRecommendation = true,
        IRealtimeNotifier? realtime = null) => new(
        db,
        new LoanComputationService(),
        new WorkflowQueueService(db, TimeProvider.System),
        Config(requireRecommendation),
        new ApplicationArrivalNotifier(db, realtime, TimeProvider.System),
        realtime,
        TimeProvider.System);

    private static CallerContext Encoder(string branchId = "BR-A") =>
        new(Guid.NewGuid(), UserRole.Encoder, branchId);

    private static ReviseLoanCommand Cmd(
        string notes = "Updated NTHP as per new payslip.",
        string? rowVersion = null,
        decimal principal = 1500m) =>
        new("Juan Dela Cruz", principal, 30, 0.05m, null, notes, rowVersion);

    [Fact]
    public async Task Handle_UpdatesLoanAndResolvesRevisions()
    {
        await using var db = Db();
        var loan = SeedForRevision(db);
        db.RevisionRequests.Add(Ebi.Alas.Api.Features.Revisions.RevisionRequest.Create(
            loan.Id, "loan-params", "Check amount", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var result = await handler.HandleAsync(loan.Id, Cmd(principal: 1500m), Encoder(), CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForRecommendation), result.Status);
        Assert.Equal(1500m, result.Principal);
        Assert.False(await db.RevisionRequests.AnyAsync(r => r.LoanApplicationId == loan.Id && r.ResolvedAt == null));
        Assert.NotEqual(string.Empty, result.RowVersion);
    }

    [Fact]
    public async Task Handle_WhenRecommendationSkipped_TransitionsToForChecking()
    {
        await using var db = Db();
        var loan = SeedForRevision(db);
        var handler = Handler(db, requireRecommendation: false);

        var result = await handler.HandleAsync(loan.Id, Cmd(), Encoder(), CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForChecking), result.Status);
        var action = await db.LoanApplicationActions.SingleAsync(a => a.LoanApplicationId == loan.Id);
        Assert.Equal(nameof(LoanStatus.ForRevision), action.FromStatus);
        Assert.Equal(nameof(LoanStatus.ForChecking), action.ToStatus);
    }

    [Fact]
    public async Task Handle_RejectsWrongStatus()
    {
        await using var db = Db();
        var loan = SeedForRevision(db);
        loan.TransitionTo(LoanStatus.ForRecommendation, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            Handler(db).HandleAsync(loan.Id, Cmd(), Encoder(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RejectsStaleRowVersion()
    {
        await using var db = Db();
        var loan = SeedForRevision(db);
        var handler = Handler(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(
                loan.Id,
                Cmd(rowVersion: Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 })),
                Encoder(),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RequiresRevisionNotes()
    {
        await using var db = Db();
        var loan = SeedForRevision(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Handler(db).HandleAsync(loan.Id, Cmd(notes: "short"), Encoder(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_HidesInaccessibleBranch()
    {
        await using var db = Db();
        var loan = SeedForRevision(db, branchId: "BR-B");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler(db).HandleAsync(loan.Id, Cmd(), Encoder(branchId: "BR-A"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NotifiesNextDeskAndPushbackRequester()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var recommender = User.Create("ben", "reyes", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Recommender, now, "AO");
        var loan = LoanApplication.Create(
            "LAM-NOTIFY", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        loan.TransitionTo(LoanStatus.ForRevision, now, recommender.Id, "PushedBack");
        db.RevisionRequests.Add(Ebi.Alas.Api.Features.Revisions.RevisionRequest.Create(
            loan.Id, "loan-params", "Fix amount", recommender.Id, now));
        db.Users.AddRange(encoder, recommender);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var handler = Handler(db, requireRecommendation: true, realtime: notifier);

        await handler.HandleAsync(
            loan.Id,
            Cmd(),
            new CallerContext(encoder.Id, UserRole.Encoder, "BR-A"),
            CancellationToken.None);

        Assert.Contains(notifier.Sent, s =>
            s.UserId == recommender.Id
            && s.Title.Contains("resubmitted", StringComparison.OrdinalIgnoreCase)
            && s.Link == $"/loans/approval/{loan.Id}");
    }

    [Fact]
    public async Task Handle_WhenRecommendationSkipped_NotifiesEvaluatorDesk()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var evaluator = User.Create("bea", "reyes", "Bea", "Reyes", "", "bea@example.com", "BR-A", UserRole.Evaluator, now, "CI");
        var loan = LoanApplication.Create(
            "LAM-NOTIFY2", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        loan.TransitionTo(LoanStatus.ForRevision, now, evaluator.Id, "PushedBack");
        db.Users.AddRange(encoder, evaluator);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var handler = Handler(db, requireRecommendation: false, realtime: notifier);

        await handler.HandleAsync(
            loan.Id,
            Cmd(),
            new CallerContext(encoder.Id, UserRole.Encoder, "BR-A"),
            CancellationToken.None);

        Assert.Contains(notifier.Sent, s =>
            s.UserId == evaluator.Id
            && s.Title.Contains("resubmitted", StringComparison.OrdinalIgnoreCase));
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

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }
}
