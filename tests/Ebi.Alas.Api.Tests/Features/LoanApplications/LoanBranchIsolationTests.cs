using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.GetLoan;
using Ebi.Alas.Api.Features.LoanApplications.ListLoans;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanBranchIsolationTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-branch-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication SeedLoan(AlasDbContext db, string branchId)
    {
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            $"LAM-{branchId}-{Guid.NewGuid():N}"[..32],
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
        db.LoanApplications.Add(loan);
        db.SaveChanges();
        return loan;
    }

    [Fact]
    public async Task GetLoan_OtherBranch_ReturnsNotFound_ForEncoder()
    {
        await using var db = Db();
        var loan = SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var handler = new GetLoanHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task LoanAccess_OtherBranch_IsDenied_ForEncoder()
    {
        await using var db = Db();
        var loan = SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        Assert.False(await LoanAccess.CanAccessAsync(db, loan.Id, caller, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            LoanAccess.RequireAccessibleAsync(db, loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task LoanAccess_SameBranch_IsAllowed_ForEncoder()
    {
        await using var db = Db();
        var loan = SeedLoan(db, "BR-A");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        Assert.True(await LoanAccess.CanAccessAsync(db, loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task ListLoans_DoesNotReturnOtherBranch_ForEncoder()
    {
        await using var db = Db();
        SeedLoan(db, "BR-A");
        SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var handler = new ListLoansHandler(db);

        var result = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 50 },
            100,
            20,
            [],
            branchId: null,
            caller,
            CancellationToken.None);

        Assert.All(result.Items, l => Assert.Equal("BR-A", l.BranchId));
    }

    [Fact]
    public async Task DocumentRemarks_OtherBranch_IsNotFound_ForEncoder()
    {
        await using var db = Db();
        var loan = SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            LoanAccess.RequireAccessibleAsync(db, loan.Id, caller, CancellationToken.None));
    }

    [Fact]
    public async Task LoanAccess_OtherBranch_IsAllowed_ForEvaluator()
    {
        await using var db = Db();
        var loan = SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "BR-A");

        Assert.True(await LoanAccess.CanAccessAsync(db, loan.Id, caller, CancellationToken.None));
        var loaded = await LoanAccess.RequireAccessibleAsync(db, loan.Id, caller, CancellationToken.None);
        Assert.Equal("BR-B", loaded.BranchId);
    }

    [Fact]
    public async Task GetLoan_OtherBranch_IsAllowed_ForEvaluator()
    {
        await using var db = Db();
        var loan = SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "BR-A");
        var handler = new GetLoanHandler(db);

        var result = await handler.HandleAsync(loan.Id, caller, CancellationToken.None);
        Assert.Equal("BR-B", result.BranchId);
    }

    [Fact]
    public async Task ListLoans_ReturnsAllBranches_ForEvaluator()
    {
        await using var db = Db();
        SeedLoan(db, "BR-A");
        SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "BR-A");
        var handler = new ListLoansHandler(db);

        var result = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 50 },
            100,
            20,
            [],
            branchId: null,
            caller,
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, l => l.BranchId == "BR-A");
        Assert.Contains(result.Items, l => l.BranchId == "BR-B");
    }

    [Fact]
    public async Task ListLoans_IgnoresBranchIdFilter_ForEvaluator()
    {
        await using var db = Db();
        SeedLoan(db, "BR-A");
        SeedLoan(db, "BR-B");
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Evaluator, "BR-A");
        var handler = new ListLoansHandler(db);

        var result = await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 50 },
            100,
            20,
            [],
            branchId: "BR-A",
            caller,
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }
}
