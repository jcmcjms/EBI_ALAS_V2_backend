using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Revisions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class TimelineActorTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"timeline-actor-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task FormRemarks_UseFormAuthorName_NotNull()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var loan = LoanApplication.Create(
            "LAM-A", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now,
            "SL", "Salary Loan");
        loan.SetFormSnapshot("""
            {
              "loans": [
                {
                  "verification": { "findings": "Verified against IDs" },
                  "deviations": {
                    "remarks": "Encoder noted missing CIBI",
                    "aoRecommendation": "Recommend approval",
                    "otherRemarks": "None",
                    "feeDeviationJustification": "Fee waived",
                    "deviationJustifications": { "LackingCIBI": "Will submit later" }
                  }
                }
              ]
            }
            """);
        db.Users.Add(encoder);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var events = await LoanReviewEndpoints.BuildTimelineEventsAsync(db, loan, CancellationToken.None);

        var remarks = events.Where(e => e.Type is "remark" or "deviationRemark").ToList();
        Assert.NotEmpty(remarks);
        Assert.All(remarks, e => Assert.Equal("Ana Cruz", e.ActorName));

        var created = Assert.Single(events, e => e.Action == "Created");
        Assert.Equal("Ana Cruz", created.ActorName);
    }

    [Fact]
    public async Task RevisionEvents_UseRequesterName()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var recommender = User.Create("ben", "reyes", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Recommender, now, "AO");
        var loan = LoanApplication.Create(
            "LAM-B", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        loan.TransitionTo(LoanStatus.ForChecking, now, encoder.Id, "Recommended");
        loan.TransitionTo(LoanStatus.ForRevision, now, recommender.Id, "PushedBack");
        db.RevisionRequests.Add(RevisionRequest.Create(
            loan.Id, "personal-info", "Please fix address", recommender.Id, now));
        db.Users.AddRange(encoder, recommender);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var events = await LoanReviewEndpoints.BuildTimelineEventsAsync(db, loan, CancellationToken.None);

        var revision = Assert.Single(events, e => e.Action == "PushedBack" && e.Type == "workflow" && e.Comment == "Please fix address");
        Assert.Equal("Ben Reyes", revision.ActorName);
    }
}
