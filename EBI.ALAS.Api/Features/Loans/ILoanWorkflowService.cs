namespace EBI.ALAS.Api.Features.Loans;
public interface ILoanWorkflowService
{
    string InitialStatus { get; }
    bool RequireRecommendation { get; }
    bool IsValidTransition(string fromStatus, string toStatus, string userRole);
    string GetRequiredRoleForTransition(string fromStatus, string toStatus);
    Dictionary<string, List<string>> GetAllowedTransitions();
    ResolvedAction ResolveAction(WorkflowAction action, string fromStatus);
}
