using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanAccessTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-access-{Guid.NewGuid():N}")
            .Options);

    private static LoanApplication Loan(string branchId, Guid createdBy)
    {
        var loan = LoanApplication.Create(
            "LAM-1", "G-1", "Client", branchId, LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, createdBy, DateTimeOffset.UtcNow);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        loan.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        return loan;
    }

    [Fact]
    public async Task GlobalApprover_CanOpenUnassignedLoanOnOtherBranch()
    {
        await using var db = Db();
        var product = User.Create(
            "ph", "hash", "P", "Head", "", "p@x.com", "BR-01",
            UserRole.Approver, DateTimeOffset.UtcNow, "Product Head");
        db.Users.Add(product);
        db.Branches.Add(Branch.Create("BR-01", "Home", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("BR-02", "Remote", "A2", DateTimeOffset.UtcNow));
        var loan = Loan("BR-02", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(product.Id, UserRole.Approver, "BR-01");
        var found = await LoanAccess.FindAccessibleAsync(db, loan.Id, caller, CancellationToken.None);

        Assert.NotNull(found);
    }

    [Fact]
    public async Task AssignedApprover_CanOpenLoanOnOtherBranch()
    {
        await using var db = Db();
        var head = User.Create(
            "bh", "hash", "B", "Head", "", "b@x.com", "BR-01",
            UserRole.Approver, DateTimeOffset.UtcNow, "Branch Head");
        db.Users.Add(head);
        db.Branches.Add(Branch.Create("BR-01", "Home", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("BR-02", "Remote", "A2", DateTimeOffset.UtcNow));
        var loan = Loan("BR-02", Guid.NewGuid());
        loan.AssignTo(head.Id, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(head.Id, UserRole.Approver, "BR-01");
        var found = await LoanAccess.FindAccessibleAsync(db, loan.Id, caller, CancellationToken.None);

        Assert.NotNull(found);
    }

    [Fact]
    public async Task BranchHead_CannotOpenOtherBranchUnassigned()
    {
        await using var db = Db();
        var head = User.Create(
            "bh", "hash", "B", "Head", "", "b@x.com", "BR-01",
            UserRole.Approver, DateTimeOffset.UtcNow, "Branch Head");
        db.Users.Add(head);
        db.Branches.Add(Branch.Create("BR-01", "Home", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("BR-02", "Remote", "A2", DateTimeOffset.UtcNow));
        var loan = Loan("BR-02", Guid.NewGuid());
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var caller = new CallerContext(head.Id, UserRole.Approver, "BR-01");
        var found = await LoanAccess.FindAccessibleAsync(db, loan.Id, caller, CancellationToken.None);

        Assert.Null(found);
    }
}
