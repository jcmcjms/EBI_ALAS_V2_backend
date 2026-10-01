namespace EBI.ALAS.Api.Features.Loans;
public interface ILoanSubmissionService
{
    Task<(LoanSubmissionResponse Response, bool Replayed)> SubmitAsync(
        SubmitLoanApplicationRequest request,
        Guid idempotencyKey,
        ClaimsPrincipal user,
        CancellationToken ct = default);
}
