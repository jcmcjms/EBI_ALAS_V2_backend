using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Deviations;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class TimelineActionActorTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"timeline-action-actor-{Guid.NewGuid():N}")
            .Options);

    private static LoanWorkflowService Service(AlasDbContext db) => new(
        db,
        new WorkflowQueueService(db, TimeProvider.System),
        null,
        new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
        TimeProvider.System);

    [Fact]
    public async Task WorkflowAction_Timeline_UsesActionCreator_NotSystem()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "hash", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var recommender = User.Create("ben", "hash", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Recommender, now, "AO");
        var loan = LoanApplication.Create(
            "LAM-ACTOR", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now,
            "SL", "Salary Loan");
        db.Users.AddRange(encoder, recommender);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(recommender.Id, UserRole.Recommender, "BR-A");
        await Service(db).RecommendAsync(loan.Id, caller, CancellationToken.None, "Docs complete");

        var action = await db.LoanApplicationActions.AsNoTracking().SingleAsync();
        Assert.Equal(recommender.Id, action.ActionByUserId);

        var events = await LoanReviewEndpoints.BuildTimelineEventsAsync(db, loan, CancellationToken.None);
        var recommended = Assert.Single(events, e => e.Action == "Recommended" && e.Id.StartsWith("workflow:action:"));
        Assert.Equal("Ben Reyes", recommended.ActorName);
    }

    [Fact]
    public async Task DeviationEvents_UseFormAuthor_NotNullActor()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "hash", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var loan = LoanApplication.Create(
            "LAM-DEV", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now,
            "SL", "Salary Loan");
        loan.SetFormSnapshot("""
            {
              "loans": [
                {
                  "deviations": {
                    "hasDeviations": true,
                    "deviationDetails": ["With history of delinquency in the latest loan availment"],
                    "deviationJustifications": { "With history of delinquency in the latest loan availment": "Will submit later" }
                  }
                }
              ]
            }
            """);
        db.Users.Add(encoder);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        await LoanDeviationSync.SyncFromFormAsync(db, loan, loan.FormJson, CancellationToken.None);

        var events = await LoanReviewEndpoints.BuildTimelineEventsAsync(db, loan, CancellationToken.None);
        var deviations = events.Where(e => e.Type == "deviation").ToList();
        Assert.NotEmpty(deviations);
        Assert.All(deviations, e => Assert.Equal("Ana Cruz", e.ActorName));
    }

    [Fact]
    public async Task HistoryActionBy_IsActionCreatorName()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "hash", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var evaluator = User.Create("ben", "hash", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Evaluator, now, "AO");
        var approver = User.Create("cara", "hash", "Cara", "Santos", "", "cara@example.com", "BR-A", UserRole.Approver, now, "Product Head");
        var loan = LoanApplication.Create(
            "LAM-HIST", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now,
            "SL", "Salary Loan");
        loan.TransitionTo(LoanStatus.ForChecking, now, encoder.Id, "Recommended");
        loan.TransitionTo(LoanStatus.ForApproval, now, evaluator.Id, "EvaluatedRecommended");
        db.Users.AddRange(encoder, evaluator, approver);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(approver.Id, UserRole.Approver, "BR-A");
        await Service(db).ApproveAsync(loan.Id, caller, CancellationToken.None, "Approved after review");

        var rows = await db.LoanApplicationActions.AsNoTracking().ToListAsync();
        var approved = Assert.Single(rows, a => a.Action == "Approved");
        Assert.Equal(approver.Id, approved.ActionByUserId);

        var events = await LoanReviewEndpoints.BuildTimelineEventsAsync(db, loan, CancellationToken.None);
        var approvedEvent = Assert.Single(events, e => e.Action == "Approved" && e.Id.StartsWith("workflow:action:"));
        Assert.Equal("Cara Santos", approvedEvent.ActorName);
    }
}
