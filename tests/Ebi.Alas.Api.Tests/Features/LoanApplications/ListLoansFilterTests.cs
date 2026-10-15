using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.ListLoans;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class ListLoansFilterTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"list-filters-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication Loan(
        string lamId,
        string clientName,
        string branchId,
        DateTimeOffset createdAt,
        string product = "Salary Loan",
        string productCode = "SL")
    {
        var loan = LoanApplication.Create(
            lamId,
            "G-1",
            clientName,
            branchId,
            LoanType.New,
            1000m,
            30,
            0.05m,
            50m,
            10m,
            940m,
            Guid.NewGuid(),
            createdAt,
            productCode,
            product);
        return loan;
    }

    private static async Task<PageResult<LoanResponse>> ListAsync(
        AlasDbContext db,
        CallerContext caller,
        string? search = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        bool myTurn = false,
        IReadOnlyList<LoanStatus>? statuses = null)
    {
        var handler = new ListLoansHandler(db);
        return await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 50 },
            maxPageSize: 100,
            defaultPageSize: 20,
            statuses: statuses ?? [],
            branchId: null,
            caller,
            CancellationToken.None,
            search,
            dateFrom,
            dateTo,
            myTurn);
    }

    [Fact]
    public async Task Search_FiltersByLamIdClientNameAndProduct()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        db.LoanApplications.Add(Loan("LAM-ALPHA", "Juan Cruz", "BR-A", now));
        db.LoanApplications.Add(Loan("LAM-BETA", "Maria Santos", "BR-A", now, product: "Business Loan", productCode: "BL"));
        db.LoanApplications.Add(Loan("LAM-GAMMA", "Pedro Reyes", "BR-A", now));
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-A");

        var byLam = await ListAsync(db, caller, search: "ALPHA");
        Assert.Equal("LAM-ALPHA", Assert.Single(byLam.Items).LamId);

        var byClient = await ListAsync(db, caller, search: "Maria");
        Assert.Equal("LAM-BETA", Assert.Single(byClient.Items).LamId);

        var byProduct = await ListAsync(db, caller, search: "Business");
        Assert.Equal("LAM-BETA", Assert.Single(byProduct.Items).LamId);

        var byCode = await ListAsync(db, caller, search: "BL");
        Assert.Equal("LAM-BETA", Assert.Single(byCode.Items).LamId);
    }

    [Fact]
    public async Task DateRange_FiltersByCreatedAt_InclusiveBounds()
    {
        await using var db = Db();
        var day1 = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var day3 = new DateTimeOffset(2026, 1, 20, 8, 0, 0, TimeSpan.Zero);
        db.LoanApplications.Add(Loan("LAM-1", "A", "BR-A", day1));
        db.LoanApplications.Add(Loan("LAM-2", "B", "BR-A", day2));
        db.LoanApplications.Add(Loan("LAM-3", "C", "BR-A", day3));
        await db.SaveChangesAsync();

        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-A");
        var page = await ListAsync(
            db,
            caller,
            dateFrom: day1,
            dateTo: day2);

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, i => i.LamId == "LAM-1");
        Assert.Contains(page.Items, i => i.LamId == "LAM-2");
        Assert.DoesNotContain(page.Items, i => i.LamId == "LAM-3");
    }

    [Fact]
    public async Task MyTurn_ReturnsOnlyLoansAssignedToCaller()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var approver = User.Create("ana", "h", "Ana", "Approver", "", "ana@example.com", "BR-A", UserRole.Approver, now, "AreaHead");

        var mine = Loan("LAM-MINE", "Mine", "BR-A", now);
        mine.TransitionTo(LoanStatus.ForChecking, now);
        mine.TransitionTo(LoanStatus.ForApproval, now, Guid.NewGuid(), "EvaluatedRecommended");
        mine.AssignTo(me, now);

        var theirs = Loan("LAM-THEIRS", "Theirs", "BR-A", now);
        theirs.TransitionTo(LoanStatus.ForChecking, now);
        theirs.TransitionTo(LoanStatus.ForApproval, now, Guid.NewGuid(), "EvaluatedRecommended");
        theirs.AssignTo(other, now);

        var unassigned = Loan("LAM-NONE", "None", "BR-A", now);

        db.LoanApplications.AddRange(mine, theirs, unassigned);
        db.Users.Add(approver);
        await db.SaveChangesAsync();

        var caller = new CallerContext(me, UserRole.Approver, "BR-A");
        var page = await ListAsync(db, caller, myTurn: true);

        Assert.Equal("LAM-MINE", Assert.Single(page.Items).LamId);
    }
}
