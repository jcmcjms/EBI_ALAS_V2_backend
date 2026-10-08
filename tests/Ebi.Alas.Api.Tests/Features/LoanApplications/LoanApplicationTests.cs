using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanApplicationTests
{
    private static LoanApplication CreateLoan(LoanStatus status = LoanStatus.ForRecommendation)
    {
        var loan = LoanApplication.Create(
            "LAM-1",
            "GRP-1",
            "Ada",
            "011",
            LoanType.New,
            10000m,
            60,
            2.5m,
            500m,
            100m,
            9900m,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        return loan;
    }

    [Fact]
    public void Create_StartsInForRecommendation()
    {
        var loan = CreateLoan();
        Assert.Equal(LoanStatus.ForRecommendation, loan.Status);
    }

    [Fact]
    public void TransitionTo_ForChecking_FromForRecommendation_Succeeds()
    {
        var loan = CreateLoan();
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        Assert.Equal(LoanStatus.ForChecking, loan.Status);
    }

    [Fact]
    public void TransitionTo_Approved_FromForRecommendation_Throws()
    {
        var loan = CreateLoan();
        Assert.Throws<InvalidOperationException>(() => loan.TransitionTo(LoanStatus.Approved, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Cancel_BeforeApproval_Succeeds()
    {
        var loan = CreateLoan();
        loan.Cancel(DateTimeOffset.UtcNow);
        Assert.Equal(LoanStatus.Cancelled, loan.Status);
    }

    [Fact]
    public void Cancel_AfterApproved_Throws()
    {
        var loan = CreateLoan();
        loan.TransitionTo(LoanStatus.ForChecking, DateTimeOffset.UtcNow);
        loan.TransitionTo(LoanStatus.ForApproval, DateTimeOffset.UtcNow);
        loan.TransitionTo(LoanStatus.Approved, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => loan.Cancel(DateTimeOffset.UtcNow));
    }
}
