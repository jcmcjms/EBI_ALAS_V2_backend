using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Branches;

public sealed class UpdateBranchStatusHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"branch-status-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task<Branch> SeedBranchAsync(AlasDbContext db, string code = "011")
    {
        var branch = Branch.Create(code, "Main", "NA", DateTimeOffset.UtcNow);
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        return branch;
    }

    private static LoanApplication NewLoan(string branchId, string lamId) =>
        LoanApplication.Create(
            lamId, "G1", "Client", branchId, LoanType.New,
            1000m, 30, 0.02m, 20m, 0m, 980m, Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Close_WithOpenLoan_Throws()
    {
        await using var db = Db();
        await SeedBranchAsync(db);
        db.LoanApplications.Add(NewLoan("011", "LAM-OPEN"));
        await db.SaveChangesAsync();

        var handler = new UpdateBranchStatusHandler(db);

        var ex = await Assert.ThrowsAsync<BranchCloseBlockedException>(() =>
            handler.HandleAsync("011", new UpdateBranchStatusRequest("Closed"), CancellationToken.None));
        Assert.Equal(1, ex.OpenLoans);
    }

    [Fact]
    public async Task Close_WithActiveUser_Throws()
    {
        await using var db = Db();
        await SeedBranchAsync(db);
        db.Users.Add(User.Create(
            "u1", "hash", "A", "B", string.Empty, null, "011", UserRole.Encoder, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var handler = new UpdateBranchStatusHandler(db);

        var ex = await Assert.ThrowsAsync<BranchCloseBlockedException>(() =>
            handler.HandleAsync("011", new UpdateBranchStatusRequest("Closed"), CancellationToken.None));
        Assert.Equal(1, ex.ActiveUsers);
    }

    [Fact]
    public async Task Close_WithNoDependencies_Succeeds()
    {
        await using var db = Db();
        await SeedBranchAsync(db);
        var handler = new UpdateBranchStatusHandler(db);

        var result = await handler.HandleAsync(
            "011", new UpdateBranchStatusRequest("Closed"), CancellationToken.None);

        Assert.Equal("Closed", result.Status);
    }

    [Fact]
    public async Task Reopen_ClosedBranch_Succeeds()
    {
        await using var db = Db();
        var branch = await SeedBranchAsync(db);
        branch.ChangeStatus(BranchStatus.Closed, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var handler = new UpdateBranchStatusHandler(db);
        var result = await handler.HandleAsync(
            "011", new UpdateBranchStatusRequest("Active"), CancellationToken.None);

        Assert.Equal("Active", result.Status);
    }

    [Fact]
    public async Task CancelledLoan_DoesNotBlockClose()
    {
        await using var db = Db();
        await SeedBranchAsync(db);
        var loan = NewLoan("011", "LAM-CANCEL");
        loan.Cancel(DateTimeOffset.UtcNow);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var handler = new UpdateBranchStatusHandler(db);
        var result = await handler.HandleAsync(
            "011", new UpdateBranchStatusRequest("Merged"), CancellationToken.None);

        Assert.Equal("Merged", result.Status);
    }

    [Fact]
    public async Task SuspendedUser_DoesNotBlockClose()
    {
        await using var db = Db();
        await SeedBranchAsync(db);
        var user = User.Create(
            "u2", "hash", "A", "B", string.Empty, null, "011", UserRole.Encoder, DateTimeOffset.UtcNow);
        user.Suspend(DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateBranchStatusHandler(db);
        var result = await handler.HandleAsync(
            "011", new UpdateBranchStatusRequest("Closed"), CancellationToken.None);

        Assert.Equal("Closed", result.Status);
    }

    [Theory]
    [InlineData(LoanStatus.Draft, true)]
    [InlineData(LoanStatus.ForApproval, true)]
    [InlineData(LoanStatus.ForDisbursement, true)]
    [InlineData(LoanStatus.Disbursed, true)]
    [InlineData(LoanStatus.OnGoing, true)]
    [InlineData(LoanStatus.Rejected, false)]
    [InlineData(LoanStatus.Cancelled, false)]
    public void IsOpen_MatchesSpec(LoanStatus status, bool expected)
    {
        Assert.Equal(expected, BranchLoanOpenStatus.IsOpen(status));
    }
}
