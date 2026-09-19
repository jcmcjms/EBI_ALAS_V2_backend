namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Loan submission service interface. Handles multi-loan submission with idempotency.
/// </summary>
public interface ILoanSubmissionService
{
    /// <summary>
    /// Persists one wizard submission as N loan applications (one per selected
    /// preloan PN), each with its own LAM ID, inside a single transaction.
    /// </summary>
    Task<(LoanSubmissionResponse Response, bool Replayed)> SubmitAsync(
        SubmitLoanApplicationRequest request,
        Guid idempotencyKey,
        ClaimsPrincipal user,
        CancellationToken ct = default);
}
