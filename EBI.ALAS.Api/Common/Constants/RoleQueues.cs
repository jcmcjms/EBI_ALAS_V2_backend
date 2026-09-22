namespace EBI.ALAS.Api.Common.Constants;

/// <summary>
/// Default monitoring queue per workflow role — the statuses where that role
/// owes the next action. This is a UI seeding convenience ONLY: it never
/// restricts what a role may read. Authorization remains <c>loans.view</c> plus
/// branch scoping in GET /api/loans; users can always widen to all statuses.
///
/// Each reviewing desk also watches the document-hold queue that feeds it:
/// a file parked at ForIncompleteDocuments returns to exactly one of these
/// desks (LoanApplication.IncompleteReturnStatus), so the desk must see it
/// waiting instead of discovering it late. Encoder and Admin monitor the
/// whole book: no default filter.
/// </summary>
public static class RoleQueues
{
    private static readonly Dictionary<string, string[]> Map = new()
    {
        [Roles.Recommender] = ["ForRecommendation", "ForIncompleteDocuments"],
        [Roles.Evaluator]   = ["ForChecking", "ForIncompleteDocuments"],
        [Roles.Approver]    = ["ForApproval", "ForIncompleteDocuments"],
    };

    public static string[] DefaultStatusesFor(string role) =>
        Map.TryGetValue(role, out var statuses) ? statuses : [];
}
