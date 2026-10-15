using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class EncoderWorkflowNotificationTests
{
    private sealed class CapturingNotifier : IRealtimeNotifier
    {
        public List<(Guid UserId, string Title, string Body)> Sent { get; } = [];

        public Task NotifyUserAsync(
            Guid notificationId,
            Guid userId,
            string title,
            string body,
            string? link,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            Sent.Add((userId, title, body));
            return Task.CompletedTask;
        }

        public Task NotifyDashboardUpdatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifyLoanAssignedAsync(
            Guid loanId,
            Guid userId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"encoder-notify-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication Loan(Guid createdBy)
    {
        var now = DateTimeOffset.UtcNow;
        return LoanApplication.Create(
            $"LAM-{Guid.NewGuid():N}"[..32],
            "G-1",
            "Client",
            "BR-A",
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
    public async Task Approve_NotifiesEncoder_NotOnlyApprover()
    {
        await using var db = Db();
        var encoder = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var loan = Loan(encoder);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        loan.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var (service, notifier) = Service(db);
        await service.ApproveAsync(
            loan.Id,
            new CallerContext(approver, UserRole.Approver, "BR-A"),
            CancellationToken.None);

        var encoderNotes = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == encoder)
            .ToListAsync();
        Assert.Contains(encoderNotes, n => n.Title.Contains("approved", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notifier.Sent, s => s.UserId == encoder);
    }

    [Fact]
    public async Task PushBack_NotifiesEncoder_AndIncludesRemarks()
    {
        await using var db = Db();
        var encoder = Guid.NewGuid();
        var evaluator = Guid.NewGuid();
        var loan = Loan(encoder);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var (service, notifier) = Service(db);
        await service.PushBackAsync(
            loan.Id,
            new CallerContext(evaluator, UserRole.Evaluator, "BR-A"),
            "Documents",
            "Please upload the latest payslip.",
            CancellationToken.None);

        var encoderNote = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == encoder)
            .SingleAsync();
        Assert.Contains("Please upload the latest payslip.", encoderNote.Body);
        Assert.Contains(notifier.Sent, s => s.UserId == encoder && s.Body.Contains("Please upload the latest payslip."));
    }
}
