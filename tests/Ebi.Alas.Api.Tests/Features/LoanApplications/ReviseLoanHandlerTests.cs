using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class ReviseLoanHandlerTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"revise-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication SeedForRevision(AlasDbContext db, string branchId = "BR-A")
    {
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-20261015-000001",
            "G-1",
            "Juan Dela Cruz",
            branchId,
            LoanType.New,
            1000m,
            30,
            0.05m,
            15m,
            50m,
            950m,
            Guid.NewGuid(),
            now,
            "A16",
            "A16 - Salary Loan");
        loan.TransitionTo(LoanStatus.ForRevision, now, loan.CreatedBy, "PushedBack");
        loan.SetFormSnapshot("""{"client":{"cisId":"CIS-1","firstName":"Juan"},"loans":[]}""");
        db.LoanApplications.Add(loan);
        db.SaveChanges();
        return loan;
    }

    [Fact]
    public void BumpRowVersion_ChangesToken()
    {
        var loan = SeedForRevision(Db());
        var before = Convert.ToBase64String(loan.RowVersion);
        loan.BumpRowVersion();
        var after = Convert.ToBase64String(loan.RowVersion);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ApplyRevision_DoesNotChangeIdentityFields()
    {
        var loan = SeedForRevision(Db());
        var lam = loan.LamId;
        var branch = loan.BranchId;
        var product = loan.ProductCode;
        loan.ApplyRevision(
            "New Name",
            2000m,
            45,
            0.06m,
            10m,
            5m,
            50m,
            "{}",
            DateTimeOffset.UtcNow);
        Assert.Equal(lam, loan.LamId);
        Assert.Equal(branch, loan.BranchId);
        Assert.Equal(product, loan.ProductCode);
    }
}
