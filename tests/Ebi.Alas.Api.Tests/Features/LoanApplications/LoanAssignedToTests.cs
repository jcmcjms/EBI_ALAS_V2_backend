using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanAssignedToTests
{
    private static LoanApplication Loan(Guid createdBy) =>
        LoanApplication.Create(
            "LAM-1", "G-1", "Client", "001", LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m, createdBy, DateTimeOffset.UtcNow, "A16", "A16");

    [Fact]
    public void AssignTo_SetsCurrentOwner()
    {
        var encoder = Guid.NewGuid();
        var evaluator = Guid.NewGuid();
        var loan = Loan(encoder);

        loan.AssignTo(evaluator, DateTimeOffset.UtcNow);

        Assert.Equal(evaluator, loan.AssignedToUserId);
    }

    [Fact]
    public void PushBack_AssignsToEncoder()
    {
        var encoder = Guid.NewGuid();
        var evaluator = Guid.NewGuid();
        var loan = Loan(encoder);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        loan.AssignTo(evaluator, DateTimeOffset.UtcNow);

        loan.TransitionTo(LoanStatus.ForRevision, DateTimeOffset.UtcNow, evaluator, "PushedBack");
        loan.AssignTo(loan.CreatedBy, DateTimeOffset.UtcNow);

        Assert.Equal(encoder, loan.AssignedToUserId);
    }

    [Fact]
    public void AssignTo_Null_ClearsOwner()
    {
        var encoder = Guid.NewGuid();
        var evaluator = Guid.NewGuid();
        var loan = Loan(encoder);
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        loan.AssignTo(evaluator, DateTimeOffset.UtcNow);

        loan.AssignTo(null, DateTimeOffset.UtcNow);

        Assert.Null(loan.AssignedToUserId);
    }
}
