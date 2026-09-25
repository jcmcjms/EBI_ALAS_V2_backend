namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Loan workflow service interface. Manages status transitions and workflow rules.
/// </summary>
public interface ILoanWorkflowService
{
    /// <summary>Status a brand-new submission lands in (follows the feature flag).</summary>
    string InitialStatus { get; }

    /// <summary>Whether the recommender step is active in the current workflow.</summary>
    bool RequireRecommendation { get; }

    bool IsValidTransition(string fromStatus, string toStatus, string userRole);
    string GetRequiredRoleForTransition(string fromStatus, string toStatus);
    Dictionary<string, List<string>> GetAllowedTransitions();

    ResolvedAction ResolveAction(WorkflowAction action, string fromStatus);
}
