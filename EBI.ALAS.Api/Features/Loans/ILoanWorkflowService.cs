namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Interface for loan workflow status transition management.
/// </summary>
public interface ILoanWorkflowService
{
    bool IsValidTransition(string fromStatus, string toStatus, string userRole);
    string GetRequiredRoleForTransition(string fromStatus, string toStatus);
    Dictionary<string, List<string>> GetAllowedTransitions();
}
