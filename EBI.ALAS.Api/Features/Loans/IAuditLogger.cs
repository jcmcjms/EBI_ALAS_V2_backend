namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Interface for audit logging of loan actions.
/// </summary>
public interface IAuditLogger
{
    Task LogActionAsync(int loanApplicationId, int actionByUserId, string action, string? fromStatus, string? toStatus, string? comments = null);
    Task<List<LoanAction>> GetLoanActionsAsync(int loanApplicationId);
}
