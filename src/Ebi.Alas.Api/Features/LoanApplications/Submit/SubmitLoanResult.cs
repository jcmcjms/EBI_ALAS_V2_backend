namespace Ebi.Alas.Api.Features.LoanApplications.Submit;

/// <summary>
/// Submission outcome. Missing documents are a normal business result (HTTP 400 from the
/// endpoint), not an exception — so the request pipeline is never interrupted.
/// </summary>
public sealed record SubmitLoanResult(
    LoanSubmissionResponse? Response,
    IReadOnlyList<string> MissingDocuments)
{
    public bool IsBlocked => MissingDocuments.Count > 0;

    public string ApplicationGroupNo => Response?.ApplicationGroupNo ?? string.Empty;

    public IReadOnlyList<CreatedLoanSummary> Loans => Response?.Loans ?? [];

    public static SubmitLoanResult Success(LoanSubmissionResponse response) => new(response, []);

    public static SubmitLoanResult Blocked(IReadOnlyList<string> missingDocuments) =>
        new(null, missingDocuments);
}
