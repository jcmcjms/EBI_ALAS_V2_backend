namespace EBI.ALAS.Api.Common.Constants;
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
