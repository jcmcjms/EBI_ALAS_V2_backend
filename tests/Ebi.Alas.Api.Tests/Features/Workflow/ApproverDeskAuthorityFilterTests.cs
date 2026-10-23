using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

public sealed class ApproverDeskAuthorityFilterTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"desk-auth-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task GetMyDesk_Coo_DoesNotSee_AreaHeadLoan()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;

        var coo = User.Create("coo", "h", "Coo", "Officer", "", "coo@x.com", "011", UserRole.Approver, now, "COO");
        var areaHead = User.Create("ah", "h", "Area", "Head", "", "ah@x.com", "011", UserRole.Approver, now, "Area Head");
        db.Users.AddRange(coo, areaHead);
        await db.SaveChangesAsync();

        var areaLoan = LoanApplication.Create(
            "LAM-AH", "G1", "Client", "011", LoanType.Reloan,
            400_000m, 365, 1.5m, 20_000m, 1_000m, 399_000m, coo.Id, now);
        areaLoan.TransitionTo(LoanStatus.ForChecking, now, coo.Id, "Submitted");
        areaLoan.TransitionTo(LoanStatus.ForApproval, now, coo.Id, "Endorsed");
        // Reloan exposure 420k, no deviations → AreaHead
        db.LoanApplications.Add(areaLoan);

        var cooLoan = LoanApplication.Create(
            "LAM-COO", "G2", "Client", "011", LoanType.New,
            1_100_000m, 365, 1.5m, 50_000m, 10_000m, 1_040_000m, coo.Id, now);
        cooLoan.TransitionTo(LoanStatus.ForChecking, now, coo.Id, "Submitted");
        cooLoan.TransitionTo(LoanStatus.ForApproval, now, coo.Id, "Endorsed");
        db.LoanApplications.Add(cooLoan);
        await db.SaveChangesAsync();

        var queue = new WorkflowQueueService(db, TimeProvider.System);
        await queue.EnqueueAsync(areaLoan.Id, WorkflowStage.Approval, "011", CancellationToken.None);
        await queue.EnqueueAsync(cooLoan.Id, WorkflowStage.Approval, "011", CancellationToken.None);

        var desk = new DeskQueueService(db, queue, TimeProvider.System);
        var result = await desk.GetMyDeskAsync(
            new CallerContext(coo.Id, UserRole.Approver, "011"),
            CancellationToken.None);

        var lamIds = result.Items
            .SelectMany(c => c.Applications)
            .Select(a => a.LamId)
            .Concat(result.CurrentClaims.SelectMany(c => c.Applications).Select(a => a.LamId))
            .ToList();

        Assert.Contains("LAM-COO", lamIds);
        Assert.DoesNotContain("LAM-AH", lamIds);
    }

    [Fact]
    public async Task GetMyDesk_AreaHead_SeesAreaHeadLoan()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;

        var areaHead = User.Create("ah", "h", "Area", "Head", "", "ah@x.com", "011", UserRole.Approver, now, "Area Head");
        var coo = User.Create("coo", "h", "Coo", "Officer", "", "coo@x.com", "011", UserRole.Approver, now, "COO");
        db.Users.AddRange(areaHead, coo);
        await db.SaveChangesAsync();

        var areaLoan = LoanApplication.Create(
            "LAM-AH", "G1", "Client", "011", LoanType.Reloan,
            400_000m, 365, 1.5m, 20_000m, 1_000m, 399_000m, areaHead.Id, now);
        areaLoan.TransitionTo(LoanStatus.ForChecking, now, areaHead.Id, "Submitted");
        areaLoan.TransitionTo(LoanStatus.ForApproval, now, areaHead.Id, "Endorsed");
        db.LoanApplications.Add(areaLoan);
        await db.SaveChangesAsync();

        var queue = new WorkflowQueueService(db, TimeProvider.System);
        await queue.EnqueueAsync(areaLoan.Id, WorkflowStage.Approval, "011", CancellationToken.None);

        var desk = new DeskQueueService(db, queue, TimeProvider.System);
        var result = await desk.GetMyDeskAsync(
            new CallerContext(areaHead.Id, UserRole.Approver, "011"),
            CancellationToken.None);

        var lamIds = result.Items
            .SelectMany(c => c.Applications)
            .Select(a => a.LamId)
            .Concat(result.CurrentClaims.SelectMany(c => c.Applications).Select(a => a.LamId))
            .ToList();

        Assert.Contains("LAM-AH", lamIds);
    }
}
