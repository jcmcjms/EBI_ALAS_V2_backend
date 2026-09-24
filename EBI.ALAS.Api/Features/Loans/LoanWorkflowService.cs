using EBI.ALAS.Api.Common.Constants;

namespace EBI.ALAS.Api.Features.Loans;

public class LoanWorkflowService : ILoanWorkflowService
{
    private readonly IWorkflowConfiguration _config;

    public LoanWorkflowService(IWorkflowConfiguration config) => _config = config;

    public string InitialStatus => _config.InitialStatus;

    public bool RequireRecommendation => _config.RequireRecommendation;

    /// <summary>
    /// Built per call from the live flag (map is tiny; no caching needed).
    /// Note what is NOT conditional: every transition OUT of ForRecommendation
    /// stays registered so in-flight loans drain even while the step is
    /// skipped, and flip-back-on resumes seamlessly.
    /// </summary>
    private Dictionary<(string From, string To), string> BuildTransitions()
    {
        var initial = _config.InitialStatus;   // ForRecommendation | ForChecking

        return new Dictionary<(string From, string To), string>
        {
            // ── Encoder cancellation — owns every in-flight status for their own loans.
            // Ownership is enforced in the endpoint, not the transition map.
            [("Draft", "Cancelled")] = Roles.Encoder,
            [("ForRecommendation", "Cancelled")] = Roles.Encoder,
            [("ForChecking", "Cancelled")] = Roles.Encoder,
            [("ForApproval", "Cancelled")] = Roles.Encoder,
            [("ForRevision", "Cancelled")] = Roles.Encoder,

            // ── Entry point follows the flag.
            [("Draft", initial)] = Roles.Encoder,

            // ── Recommender edges: always live (backlog drain + flip-back-on).
            [("ForRecommendation", "ForChecking")] = Roles.Recommender,
            [("ForRecommendation", "ForRevision")] = Roles.Recommender,

            // ── Revision resubmission re-enters at the current entry point.
            [("ForRevision", initial)] = Roles.Encoder,

            // ── Unconditional downstream edges.
            [("ForChecking", "ForApproval")] = Roles.Evaluator,
            [("ForChecking", "ForRevision")] = Roles.Evaluator,

            // ── Document flag: reviewing role may flag at their desk (manual entry;
            //    there is no automatic hold anymore).
            [("ForRecommendation", "ForIncompleteDocuments")] = Roles.Recommender,
            [("ForChecking", "ForIncompleteDocuments")] = Roles.Evaluator,
            [("ForApproval", "ForIncompleteDocuments")] = Roles.Approver,

            // ── Encoder resubmits or cancels from the incomplete-docs queue.
            [("ForIncompleteDocuments", "ForChecking")] = Roles.Encoder,
            [("ForIncompleteDocuments", "Cancelled")] = Roles.Encoder,

            // ── Evaluator edges from the flagged desk: identical to ForChecking.
            //    Recommend / Not Recommend → ForApproval with verdict,
            //    Push back → ForRevision. Remarks remain mandatory on every edge,
            //    so the written justification lives in the normal audit trail.
            [("ForIncompleteDocuments", "ForApproval")] = Roles.Evaluator,
            [("ForIncompleteDocuments", "ForRevision")] = Roles.Evaluator,

            // ── Admin escape hatch (document server wrong/unavailable).
            [("ForIncompleteDocuments", "ForRecommendation")] = Roles.Admin,
            [("ForApproval", "Approved")] = Roles.Approver,
            [("ForApproval", "Rejected")] = Roles.Approver,
            [("ForApproval", "ForRevision")] = Roles.Approver,

            // ── Post-approval flow.
            [("Approved", "ForDisbursement")] = Roles.Admin,
            [("ForDisbursement", "Disbursed")] = Roles.Admin,
            [("Disbursed", "OnGoing")] = Roles.Admin,
        };
    }

    public bool IsValidTransition(string fromStatus, string toStatus, string userRole)
    {
        // System actor: auto-return edges from ForIncompleteDocuments to any
        // review desk. Least-privilege: cannot perform any other transition.
        if (userRole == Roles.System)
            return fromStatus == "ForIncompleteDocuments"
                && toStatus is "ForChecking" or "ForRecommendation" or "ForApproval";

        if (!BuildTransitions().TryGetValue((fromStatus, toStatus), out var requiredRole))
            return false;

        // Admin can perform any transition
        return userRole == Roles.Admin || requiredRole == userRole;
    }

    public string GetRequiredRoleForTransition(string fromStatus, string toStatus) =>
        BuildTransitions().TryGetValue((fromStatus, toStatus), out var role) ? role : string.Empty;

    public Dictionary<string, List<string>> GetAllowedTransitions()
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var t in BuildTransitions())
        {
            if (!result.TryGetValue(t.Key.From, out var list))
                result[t.Key.From] = list = [];
            list.Add(t.Key.To);
        }
        return result;
    }
}
