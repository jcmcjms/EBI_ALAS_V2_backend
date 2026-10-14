using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.ListLoans;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class ListLoansAssignedToTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"list-assigned-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task List_IncludesAssignedToName_ForApprovalLoan()
    {
        await using var db = Db();
        var encoder = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var approver = User.Create("ana", "h", "Ana", "Approver", "", "ana@example.com", "BR-A", UserRole.Approver, now, "AreaHead");
        var loan = LoanApplication.Create(
            "LAM-A", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder, now);
        loan.TransitionTo(LoanStatus.ForChecking, now);
        loan.TransitionTo(LoanStatus.ForApproval, now, Guid.NewGuid(), "EvaluatedRecommended");
        loan.AssignTo(approver.Id, now);
        db.LoanApplications.Add(loan);
        db.Users.Add(approver);
        await db.SaveChangesAsync();

        var handler = new ListLoansHandler(db);
        var page = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 20 },
            maxPageSize: 100,
            defaultPageSize: 20,
            statuses: [],
            branchId: null,
            new CallerContext(encoder, UserRole.Admin, "BR-A"),
            CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(approver.Id, item.AssignedToUserId);
        Assert.Equal("Ana Approver", item.AssignedToName);
    }
}
