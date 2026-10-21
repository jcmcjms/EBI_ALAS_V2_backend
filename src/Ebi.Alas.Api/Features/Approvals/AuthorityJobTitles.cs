namespace Ebi.Alas.Api.Features.Approvals;

public static class AuthorityJobTitles
{
    private static readonly Dictionary<string, AuthorityLevel> MapByCompact =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BranchHead"] = AuthorityLevel.BranchHead,
            ["BranchManager"] = AuthorityLevel.BranchHead,
            ["SeniorBranchHead"] = AuthorityLevel.BranchHead,
            ["OIC"] = AuthorityLevel.BranchHead,
            ["BranchOIC"] = AuthorityLevel.BranchHead,
            ["OfficerInCharge"] = AuthorityLevel.BranchHead,
            ["AreaHead"] = AuthorityLevel.AreaHead,
            ["AreaHeadLevelH"] = AuthorityLevel.AreaHead,
            ["RBGHead"] = AuthorityLevel.RbgProductCredit,
            ["ProductHead"] = AuthorityLevel.RbgProductCredit,
            ["CreditHead"] = AuthorityLevel.RbgProductCredit,
            ["CEOPresident"] = AuthorityLevel.PresidentCoo,
            ["PresidentCEO"] = AuthorityLevel.PresidentCoo,
            ["PresidentChiefExecutiveOfficer"] = AuthorityLevel.PresidentCoo,
            ["COO"] = AuthorityLevel.PresidentCoo,
            ["ChiefOperationOfficer"] = AuthorityLevel.PresidentCoo,
            ["CreComChair"] = AuthorityLevel.PresidentCoo,
            ["BODLevelA"] = AuthorityLevel.CreComBod,
            ["BoardCreditCommittee"] = AuthorityLevel.CreComBod,
            ["CreCom"] = AuthorityLevel.CreComBod,
        };

    public static AuthorityLevel? Map(string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            return null;
        }

        return MapByCompact.TryGetValue(Compact(jobTitle), out var level) ? level : null;
    }

    public static string Compact(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());
}
