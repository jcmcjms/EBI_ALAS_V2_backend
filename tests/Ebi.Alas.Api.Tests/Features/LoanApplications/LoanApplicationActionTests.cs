using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanApplicationActionTests
{
    [Fact]
    public void Create_AppendsImmutableLogRow()
    {
        var loanId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;

        var action = LoanApplicationAction.Create(
            loanId, actor, "EvaluatedRecommended", "ForChecking", "ForApproval", "ok", at);

        Assert.Equal(loanId, action.LoanApplicationId);
        Assert.Equal(actor, action.ActionByUserId);
        Assert.Equal("EvaluatedRecommended", action.Action);
        Assert.Equal("ForChecking", action.FromStatus);
        Assert.Equal("ForApproval", action.ToStatus);
        Assert.Equal(at, action.ActionDate);
    }

    [Fact]
    public void Create_RequiresActionName()
    {
        Assert.Throws<ArgumentException>(() =>
            LoanApplicationAction.Create(Guid.NewGuid(), Guid.NewGuid(), " ", null, null, null, DateTimeOffset.UtcNow));
    }
}
