namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Audit logger for loan workflow actions.
/// Records every status transition with the acting user and timestamp.
/// </summary>
public interface IAuditLogger
{
    Task LogActionAsync(int loanApplicationId, int actionByUserId, string action, string? fromStatus, string? toStatus, string? comments = null);
    Task<List<LoanAction>> GetLoanActionsAsync(int loanApplicationId);
}
