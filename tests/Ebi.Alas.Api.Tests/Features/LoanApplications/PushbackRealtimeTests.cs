using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class PushbackRealtimeTests
{
    private sealed record UserNotify(
        Guid UserId,
        string Title,
        string? Link);

    private sealed record AssignedNotify(Guid LoanId, Guid UserId);

    private sealed class FakeNotifier : IRealtimeNotifier
    {
        public List<UserNotify> UserNotifications { get; } = [];
        public List<AssignedNotify> Assignments { get; } = [];

        public Task NotifyUserAsync(
            Guid notificationId,
            Guid userId,
            string title,
            string body,
            string? link,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            UserNotifications.Add(new UserNotify(userId, title, link));
            return Task.CompletedTask;
        }

        public Task NotifyDashboardUpdatedAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task NotifyLoanAssignedAsync(
            Guid loanId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            Assignments.Add(new AssignedNotify(loanId, userId));
            return Task.CompletedTask;
        }
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"pushback-rt-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task<(LoanApplication Loan, User Encoder, User Reviewer, FakeNotifier Notifier, LoanWorkflowService Service)> SetupAsync(AlasDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var reviewer = User.Create("ben", "reyes", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Recommender, now, "AO");
        var loan = LoanApplication.Create(
            "LAM-RT", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        loan.TransitionTo(LoanStatus.ForChecking, now, encoder.Id, "Recommended");

        db.Users.AddRange(encoder, reviewer);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var notifier = new FakeNotifier();
        var service = new LoanWorkflowService(
            db,
            new WorkflowQueueService(db, TimeProvider.System),
            notifier,
            new ApplicationArrivalNotifier(db, notifier, TimeProvider.System),
            TimeProvider.System);

        return (loan, encoder, reviewer, notifier, service);
    }

    [Fact]
    public async Task PushBack_NotifiesEncoderWithRevisionLink_AndBroadcastsAssignment()
    {
        await using var db = Db();
        var (loan, encoder, reviewer, notifier, service) = await SetupAsync(db);
        var caller = new CallerContext(reviewer.Id, UserRole.Recommender, "BR-A");

        await service.PushBackAsync(loan.Id, caller, "personal-info", "Fix address", CancellationToken.None);

        var encoderNotify = Assert.Single(
            notifier.UserNotifications,
            n => n.UserId == encoder.Id && n.Title == "Application returned for revision");
        Assert.Equal($"/loans/revision/{loan.Id}", encoderNotify.Link);

        var assignment = Assert.Single(notifier.Assignments);
        Assert.Equal(loan.Id, assignment.LoanId);
        Assert.Equal(encoder.Id, assignment.UserId);
    }
}
