using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.GetLoan;
using Ebi.Alas.Api.Features.LoanApplications.ListLoans;
using Ebi.Alas.Api.Features.Pagination;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanAccessTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-access-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication Loan(string branchId)
    {
        var now = DateTimeOffset.UtcNow;
        return LoanApplication.Create(
            $"LAM-{Guid.NewGuid():N}"[..32],
            "G-1",
            "Client",
            branchId,
            LoanType.New,
            1000m,
            30,
            0.05m,
            50m,
            10m,
            940m,
            Guid.NewGuid(),
            now);
    }

    [Fact]
    public async Task GetLoan_OtherBranch_ThrowsNotFound()
    {
        await using var db = Db();
        var loan = Loan("BR-A");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var handler = new GetLoanHandler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-B");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task GetLoan_SameBranch_ReturnsLoan()
    {
        await using var db = Db();
        var loan = Loan("BR-A");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var handler = new GetLoanHandler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var result = await handler.HandleAsync(loan.Id, caller, CancellationToken.None);
        Assert.Equal(loan.LamId, result.LamId);
    }

    [Fact]
    public async Task GetLoan_AdminAnyBranch_ReturnsLoan()
    {
        await using var db = Db();
        var loan = Loan("BR-A");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var handler = new GetLoanHandler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-Z");

        var result = await handler.HandleAsync(loan.Id, caller, CancellationToken.None);
        Assert.Equal(loan.LamId, result.LamId);
    }

    [Fact]
    public async Task GetLoanByLamId_OtherBranch_ThrowsNotFound()
    {
        await using var db = Db();
        var loan = Loan("BR-A");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var handler = new GetLoanByLamIdHandler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-B");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(loan.LamId, caller, CancellationToken.None));
    }

    [Fact]
    public async Task ListLoans_NonAdmin_IgnoresClientBranchFilter_UsesCallerBranch()
    {
        await using var db = Db();
        db.LoanApplications.Add(Loan("BR-A"));
        db.LoanApplications.Add(Loan("BR-B"));
        await db.SaveChangesAsync();

        var handler = new ListLoansHandler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var page = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 20 },
            maxPageSize: 100,
            defaultPageSize: 20,
            status: null,
            branchId: "BR-B",
            caller,
            CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task ListLoans_Admin_MayFilterByBranch()
    {
        await using var db = Db();
        db.LoanApplications.Add(Loan("BR-A"));
        db.LoanApplications.Add(Loan("BR-B"));
        await db.SaveChangesAsync();

        var handler = new ListLoansHandler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");

        var page = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 20 },
            maxPageSize: 100,
            defaultPageSize: 20,
            status: null,
            branchId: "BR-B",
            caller,
            CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
    }
}
