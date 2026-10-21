using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApproverAssignmentServiceTests
{
    private static AlasDbContext Db() =>
        new(new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"assign-{Guid.NewGuid():N}")
            .Options);

    private static LoanApplication Loan(
        LoanType type,
        decimal principal,
        string branchId)
    {
        return LoanApplication.Create(
            "LAM-1", "G-1", "Client", branchId, type,
            principal, 30, 0.05m, 10m, 50m, principal - 60m,
            Guid.NewGuid(), DateTimeOffset.UtcNow, "A16", "A16");
    }

    private static async Task<User> SeedApprover(
        AlasDbContext db,
        string jobTitle,
        string branchId,
        string user = "u",
        bool available = true)
    {
        var created = User.Create(
            user + AuthorityJobTitles.Compact(jobTitle), "hash", "First", "Last", "M", $"{user}@x.com",
            branchId, UserRole.Approver, DateTimeOffset.UtcNow, jobTitle);
        if (!available)
        {
            created.SetAvailable(false, DateTimeOffset.UtcNow);
        }

        db.Users.Add(created);
        await db.SaveChangesAsync();
        return created;
    }

    [Fact]
    public async Task Pick_RelosnSmall_SameBranchBranchHead()
    {
        await using var db = Db();
        var head = await SeedApprover(db, "Branch Head", "011");
        await SeedApprover(db, "Area Head", "011");
        var loan = Loan(LoanType.Reloan, 100_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Equal(head.Id, result.AssignedUserId);
        Assert.Equal(AuthorityLevel.BranchHead, result.RequiredLevel);
        Assert.Null(result.NoAuthorityReason);
    }

    [Fact]
    public async Task Pick_PrefersLeastLoaded()
    {
        await using var db = Db();
        var busy = await SeedApprover(db, "Branch Head", "011", "busy");
        var free = await SeedApprover(db, "Branch Head", "011", "free");
        var other = Loan(LoanType.Reloan, 50_000m, "011");
        other.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        other.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        other.AssignTo(busy.Id, DateTimeOffset.UtcNow);
        db.LoanApplications.Add(other);

        var loan = Loan(LoanType.Reloan, 80_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Equal(free.Id, result.AssignedUserId);
    }

    [Fact]
    public async Task Pick_BranchScope_DoesNotUseOtherBranchHead()
    {
        await using var db = Db();
        await SeedApprover(db, "Branch Head", "012");
        var area = await SeedApprover(db, "Area Head", "012");
        db.Branches.Add(Branch.Create("011", "Home", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("012", "Other", "A1", DateTimeOffset.UtcNow));
        var loan = Loan(LoanType.Reloan, 100_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Equal(area.Id, result.AssignedUserId);
        Assert.Equal(AuthorityLevel.BranchHead, result.EscalatedFromLevel);
    }

    [Fact]
    public async Task Pick_RbgProductCreditHead_SharedPool_NotSingleAssignee()
    {
        await using var db = Db();
        var rbg = await SeedApprover(db, "RBG Head", "011", "rbg");
        var product = await SeedApprover(db, "Product Head", "011", "prod");
        var credit = await SeedApprover(db, "Credit Head", "011", "cred");
        var loan = Loan(LoanType.New, 500_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Null(result.AssignedUserId);
        Assert.Equal(AuthorityLevel.RbgProductCredit, result.RequiredLevel);
        Assert.Null(result.NoAuthorityReason);
        Assert.Equal(3, result.SharedPool.Count);
        Assert.Contains(result.SharedPool, c => c.UserId == rbg.Id);
        Assert.Contains(result.SharedPool, c => c.UserId == product.Id);
        Assert.Contains(result.SharedPool, c => c.UserId == credit.Id);
    }

    [Fact]
    public async Task Pick_SkipsUnavailable_AndEscalatesToAvailableHigherLevel()
    {
        await using var db = Db();
        await SeedApprover(db, "Branch Head", "011", "busy", available: false);
        var president = await SeedApprover(db, "President & CEO", "011", "ceo");
        var loan = Loan(LoanType.Reloan, 100_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Equal(president.Id, result.AssignedUserId);
        Assert.Equal(AuthorityLevel.BranchHead, result.EscalatedFromLevel);
    }

    [Fact]
    public async Task Pick_SharedPool_ExcludesUnavailableOfficers()
    {
        await using var db = Db();
        var rbg = await SeedApprover(db, "RBG Head", "011", "rbg", available: false);
        var product = await SeedApprover(db, "Product Head", "011", "prod");
        var credit = await SeedApprover(db, "Credit Head", "011", "cred");
        var loan = Loan(LoanType.New, 400_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Null(result.AssignedUserId);
        Assert.Equal(2, result.SharedPool.Count);
        Assert.DoesNotContain(result.SharedPool, c => c.UserId == rbg.Id);
        Assert.Contains(result.SharedPool, c => c.UserId == product.Id);
        Assert.Contains(result.SharedPool, c => c.UserId == credit.Id);
    }

    [Fact]
    public async Task Pick_CreCom_ReturnsNoAssigneeWithReason()
    {
        await using var db = Db();
        await SeedApprover(db, "President & CEO", "011");
        var loan = Loan(LoanType.New, 2_000_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Null(result.AssignedUserId);
        Assert.Equal(AuthorityLevel.CreComBod, result.RequiredLevel);
        Assert.NotNull(result.NoAuthorityReason);
    }

    [Fact]
    public async Task Pick_NoCandidates_EscalatesToPresident()
    {
        await using var db = Db();
        var president = await SeedApprover(db, "President & CEO", "011");
        var loan = Loan(LoanType.Reloan, 100_000m, "011");
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var result = await new ApproverAssignmentService(db).PickAsync(loan, CancellationToken.None);

        Assert.Equal(president.Id, result.AssignedUserId);
        Assert.Equal(AuthorityLevel.BranchHead, result.EscalatedFromLevel);
    }
}
