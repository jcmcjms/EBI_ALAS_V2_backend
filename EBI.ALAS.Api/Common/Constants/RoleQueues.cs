namespace EBI.ALAS.Api.Common.Constants;

/// <summary>
/// Default monitoring queue per workflow role — the statuses where that role
/// owes the next action. This is a UI seeding convenience ONLY: it never
/// restricts what a role may read. Authorization remains <c>loans.view</c> plus
/// branch scoping in GET /api/loans; users can always widen to all statuses.
///
/// Document deficiency is a data flag, not a status. Flagged files stay at
/// their real desk (ForRecommendation, ForChecking, ForApproval) and appear
/// in the normal queue with an amber flag chip. No special status tracking.
/// </summary>
public static class RoleQueues
{
    private static readonly Dictionary<string, string[]> Map = new()
    {
        [Roles.Recommender] = ["ForRecommendation"],
        [Roles.Evaluator]   = ["ForChecking"],
        [Roles.Approver]    = ["ForApproval"],
    };

    public static string[] DefaultStatusesFor(string role) =>
        Map.TryGetValue(role, out var statuses) ? statuses : [];
}
