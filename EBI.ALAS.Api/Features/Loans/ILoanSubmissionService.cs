using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Loans;

public interface ILoanSubmissionService
{
    /// <summary>
    /// Persists one wizard submission as N loan applications (one per selected
    /// preloan PN), each with its own LAM ID, inside a single transaction.
    /// <c>Replayed</c> is true when the idempotency key was already used and
    /// the stored response is returned instead of creating anything.
    /// </summary>
    Task<(LoanSubmissionResponse Response, bool Replayed)> SubmitAsync(
        SubmitLoanApplicationRequest request,
        Guid idempotencyKey,
        ClaimsPrincipal user,
        CancellationToken ct = default);
}