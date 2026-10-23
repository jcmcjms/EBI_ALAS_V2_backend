namespace Ebi.Alas.Api.Features.Approvals;

public static class AuthorityJobTitles
{
    private static readonly Dictionary<string, AuthorityLevel> MapByCompact =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BranchHead"] = AuthorityLevel.BranchHead,
            ["BranchManager"] = AuthorityLevel.BranchHead,
            ["SeniorBranchHead"] = AuthorityLevel.BranchHead,
            ["OIC"] = AuthorityLevel.OfficerInCharge,
            ["BranchOIC"] = AuthorityLevel.OfficerInCharge,
            ["OfficerInCharge"] = AuthorityLevel.OfficerInCharge,
            ["AreaHead"] = AuthorityLevel.AreaHead,
            ["AreaHeadLevelH"] = AuthorityLevel.AreaHead,
            ["RBGHead"] = AuthorityLevel.RbgHead,
            ["ProductHead"] = AuthorityLevel.ProductHead,
            ["CreditHead"] = AuthorityLevel.CreditHead,
            ["CEOPresident"] = AuthorityLevel.PresidentCeo,
            ["PresidentCEO"] = AuthorityLevel.PresidentCeo,
            ["PresidentChiefExecutiveOfficer"] = AuthorityLevel.PresidentCeo,
            ["COO"] = AuthorityLevel.Coo,
            ["ChiefOperationOfficer"] = AuthorityLevel.Coo,
            ["ChiefOperatingOfficer"] = AuthorityLevel.Coo,
            ["CreComChair"] = AuthorityLevel.PresidentCeo,
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
