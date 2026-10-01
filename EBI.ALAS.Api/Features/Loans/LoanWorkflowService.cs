using EBI.ALAS.Api.Common.Constants;
namespace EBI.ALAS.Api.Features.Loans;
public class LoanWorkflowService : ILoanWorkflowService
{
    private readonly IWorkflowConfiguration _config;
    public LoanWorkflowService(IWorkflowConfiguration config) => _config = config;
    public string InitialStatus => _config.InitialStatus;
    public bool RequireRecommendation => _config.RequireRecommendation;
    private Dictionary<(string From, string To), string> BuildTransitions()
    {
        var initial = _config.InitialStatus;
        return new Dictionary<(string From, string To), string>
        {
            [("Draft", "Cancelled")] = Roles.Encoder,
            [("ForRecommendation", "Cancelled")] = Roles.Encoder,
            [("ForChecking", "Cancelled")] = Roles.Encoder,
            [("ForApproval", "Cancelled")] = Roles.Encoder,
            [("ForRevision", "Cancelled")] = Roles.Encoder,
            [("Draft", initial)] = Roles.Encoder,
            [("ForRecommendation", "ForChecking")] = Roles.Recommender,
            [("ForRecommendation", "ForRevision")] = Roles.Recommender,
            [("ForRevision", initial)] = Roles.Encoder,
            [("ForChecking", "ForApproval")] = Roles.Evaluator,
            [("ForChecking", "ForRevision")] = Roles.Evaluator,
            [("ForApproval", "Approved")] = Roles.Approver,
            [("ForApproval", "Rejected")] = Roles.Approver,
            [("ForApproval", "ForRevision")] = Roles.Approver,
            [("Approved", "ForDisbursement")] = Roles.Admin,
            [("ForDisbursement", "Disbursed")] = Roles.Admin,
            [("Disbursed", "OnGoing")] = Roles.Admin,
            [("ForIncompleteDocuments", "ForRecommendation")] = $"{Roles.System}|{Roles.Admin}",
            [("ForIncompleteDocuments", "ForChecking")] = $"{Roles.System}|{Roles.Encoder}",
            [("ForIncompleteDocuments", "ForApproval")] = $"{Roles.System}|{Roles.Evaluator}",
            [("ForIncompleteDocuments", "ForRevision")] = $"{Roles.Evaluator}|{Roles.Admin}",
            [("ForIncompleteDocuments", "Cancelled")] = Roles.Encoder,
            [("ForChecking", "ForIncompleteDocuments")] = Roles.Evaluator,
        };
    }
    public bool IsValidTransition(string fromStatus, string toStatus, string userRole)
    {
        if (!BuildTransitions().TryGetValue((fromStatus, toStatus), out var requiredRole))
            return false;
        return userRole == Roles.Admin || requiredRole.Split('|').Contains(userRole);
    }
    public string GetRequiredRoleForTransition(string fromStatus, string toStatus)
    {
        if (!BuildTransitions().TryGetValue((fromStatus, toStatus), out var role))
            return string.Empty;
        return role
            .Split('|')
            .FirstOrDefault(r => r != Roles.System, role);
    }
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
    public ResolvedAction ResolveAction(WorkflowAction action, string fromStatus) =>
        (action, fromStatus) switch
        {
            (WorkflowAction.Recommend, "ForRecommendation") => new ResolvedAction("ForChecking", null),
            (WorkflowAction.Recommend, "ForChecking") => new ResolvedAction("ForApproval", "Recommended"),
            (WorkflowAction.NotRecommend, "ForChecking") => new ResolvedAction("ForApproval", "NotRecommended"),
            (WorkflowAction.PushBack, "ForRecommendation" or "ForChecking" or "ForApproval") => new ResolvedAction("ForRevision", null),
            (WorkflowAction.Approve, "ForApproval") => new ResolvedAction("Approved", null),
            (WorkflowAction.Reject, "ForApproval") => new ResolvedAction("Rejected", null),
            (WorkflowAction.ReturnForRevision, "ForApproval") => new ResolvedAction("ForRevision", null),
            _ => throw new InvalidOperationException($"{action} is not available at the {fromStatus} desk."),
        };
}
