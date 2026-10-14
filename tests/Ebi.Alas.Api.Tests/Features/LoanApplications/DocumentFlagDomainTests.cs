using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class DocumentFlagDomainTests
{
    private static LoanApplication Loan() => LoanApplication.Create(
        "LAM-1",
        "G-1",
        "Client",
        "BR-A",
        LoanType.New,
        1000m,
        30,
        0.05m,
        10m,
        50m,
        940m,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow);

    [Fact]
    public void FlagDocuments_SetsFlagFields()
    {
        var loan = Loan();
        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        loan.FlagDocuments(2, "Missing IDs", actor, now);

        Assert.Equal(now, loan.DocumentsFlaggedAt);
        Assert.Equal(actor, loan.DocumentsFlaggedById);
        Assert.Equal("Missing IDs", loan.DocumentFlagReason);
        Assert.Equal(2, loan.DocumentFlagMissingCount);
    }

    [Fact]
    public void FlagDocuments_RequiresAtLeastOneMissingCode()
    {
        var loan = Loan();

        Assert.Throws<ArgumentException>(() =>
            loan.FlagDocuments(0, "reason", Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ClearDocumentFlag_ResetsFlagFields()
    {
        var loan = Loan();
        loan.FlagDocuments(1, "Missing ID", Guid.NewGuid(), DateTimeOffset.UtcNow);

        loan.ClearDocumentFlag(DateTimeOffset.UtcNow);

        Assert.Null(loan.DocumentsFlaggedAt);
        Assert.Null(loan.DocumentsFlaggedById);
        Assert.Null(loan.DocumentFlagReason);
        Assert.Equal(0, loan.DocumentFlagMissingCount);
    }
}
