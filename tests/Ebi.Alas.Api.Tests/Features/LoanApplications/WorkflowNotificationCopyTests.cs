using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class WorkflowNotificationCopyTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"notif-copy-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task Evaluate_NotificationUsesProductLanguage()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        var loan = LoanApplication.Create(
            "LAM-20261014-000001", "G-1", "Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, userId, DateTimeOffset.UtcNow, "A16", "A16");
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var service = new Ebi.Alas.Api.Features.LoanApplications.WorkflowActions.LoanWorkflowService(
            db,
            new WorkflowQueueService(db, TimeProvider.System),
            null,
            new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
            TimeProvider.System);
        await service.EvaluateAsync(
            loan.Id, true, new CallerContext(userId, UserRole.Evaluator, "001"), CancellationToken.None);

        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("recommend=", notification.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("True", notification.Title, StringComparison.Ordinal);
        Assert.Contains("recommended", notification.Title, StringComparison.OrdinalIgnoreCase);
    }
}
