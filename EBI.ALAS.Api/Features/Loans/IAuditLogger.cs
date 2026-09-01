namespace EBI.ALAS.Api.Features.Loans;
public interface IAuditLogger
{
    Task LogActionAsync(int loanApplicationId, int actionByUserId, string action, string? fromStatus, string? toStatus, string? comments = null);
    Task<List<LoanAction>> GetLoanActionsAsync(int loanApplicationId);
}
