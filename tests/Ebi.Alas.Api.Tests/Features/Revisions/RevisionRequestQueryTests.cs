using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Revisions;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Revisions;

public sealed class RevisionRequestQueryTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"rev-req-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task List_ReturnsOpenRevisionWithSectionAndComment()
    {
        await using var db = Db();
        var encoder = Guid.NewGuid();
        var evaluator = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-R", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder, now);
        db.LoanApplications.Add(loan);
        var author = User.Create("ev", "h", "Eva", "Luator", "", "e@x.com", "BR-A", UserRole.Evaluator, now);
        db.Users.Add(author);
        await db.SaveChangesAsync();
        db.RevisionRequests.Add(RevisionRequest.Create(
            loan.Id, "cis-lookup,personal-info", "Please fix CIS.", author.Id, now));
        await db.SaveChangesAsync();

        var service = new Ebi.Alas.Api.Features.Revisions.RevisionRequestService(db, TimeProvider.System);
        var items = await service.ListAsync(loan.Id, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.False(item.IsResolved);
        Assert.Equal("Please fix CIS.", item.OverallComments);
        Assert.Equal(2, item.Sections.Count);
        Assert.Contains(item.Sections, s => s.SectionId == "cis-lookup");
    }

    [Fact]
    public async Task Resolve_MarksResolved()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-R2", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);
        db.LoanApplications.Add(loan);
        var req = RevisionRequest.Create(loan.Id, "deviations", "Fix this.", Guid.NewGuid(), now);
        db.RevisionRequests.Add(req);
        await db.SaveChangesAsync();

        var service = new Ebi.Alas.Api.Features.Revisions.RevisionRequestService(db, TimeProvider.System);
        var ok = await service.ResolveAsync(loan.Id, req.Id, CancellationToken.None);

        Assert.True(ok);
        var reloaded = await db.RevisionRequests.AsNoTracking().SingleAsync();
        Assert.NotNull(reloaded.ResolvedAt);
    }
}
