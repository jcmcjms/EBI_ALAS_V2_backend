using EBI.ALAS.Api.Common.Constants;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Loan workflow service that manages status transitions and validates allowed transitions.
/// </summary>
public class LoanWorkflowService : ILoanWorkflowService
{
    private static readonly Dictionary<(string From, string To), string> ValidTransitions = new()
    {
        [("Draft", "ForRecommendation")] = Roles.Encoder,
        [("ForRecommendation", "ForChecking")] = Roles.Recommender,
        [("ForChecking", "ForApproval")] = Roles.Evaluator,
        [("ForApproval", "Approved")] = Roles.Approver,
        [("ForApproval", "Rejected")] = Roles.Approver,
        [("ForApproval", "ForRevision")] = Roles.Approver,
        [("ForRevision", "ForRecommendation")] = Roles.Encoder,
        [("Approved", "ForDisbursement")] = Roles.Admin,
        [("ForDisbursement", "Disbursed")] = Roles.Admin,
        [("Disbursed", "OnGoing")] = Roles.Admin
    };

    public bool IsValidTransition(string fromStatus, string toStatus, string userRole)
    {
        var transitionKey = (fromStatus, toStatus);
        
        if (!ValidTransitions.TryGetValue(transitionKey, out var requiredRole))
        {
            return false;
        }

        // Admin can perform any transition
        if (userRole == Roles.Admin)
        {
            return true;
        }

        return requiredRole == userRole;
    }

    public string GetRequiredRoleForTransition(string fromStatus, string toStatus)
    {
        var transitionKey = (fromStatus, toStatus);
        
        if (ValidTransitions.TryGetValue(transitionKey, out var requiredRole))
        {
            return requiredRole;
        }

        return string.Empty;
    }

    public Dictionary<string, List<string>> GetAllowedTransitions()
    {
        var result = new Dictionary<string, List<string>>();

        foreach (var transition in ValidTransitions)
        {
            if (!result.ContainsKey(transition.Key.From))
            {
                result[transition.Key.From] = new List<string>();
            }

            result[transition.Key.From].Add(transition.Key.To);
        }

        return result;
    }
}
