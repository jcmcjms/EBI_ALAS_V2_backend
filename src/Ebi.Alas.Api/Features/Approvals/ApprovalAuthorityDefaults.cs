namespace Ebi.Alas.Api.Features.Approvals;

public sealed record ApprovalAuthorityDefault(
    AuthorityLevel Level,
    string DisplayName,
    string Key,
    int Tier,
    bool AllowNew,
    bool AllowRenewal,
    int MaxSeverity,
    decimal MaxTotalExposure,
    int ScopeType);

/// <summary>
/// Canonical officer authority catalog. Shared by the seeder and import
/// assignment so the two paths cannot drift. <see cref="ApprovalAuthorityDefault.Key"/>
/// is the slug of the original display name and is never rewritten on rename.
/// </summary>
public static class ApprovalAuthorityDefaults
{
    public static readonly IReadOnlyList<ApprovalAuthorityDefault> All =
    [
        new(AuthorityLevel.BranchHead, "Branch Head", "branch-head", 1, false, true, 0, 300_000m, AuthorityScopes.Branch),
        new(AuthorityLevel.OfficerInCharge, "Officer In Charge", "officer-in-charge", 1, false, true, 0, 300_000m, AuthorityScopes.Branch),
        new(AuthorityLevel.AreaHead, "Area Head", "area-head", 2, false, true, 0, 600_000m, AuthorityScopes.Area),
        new(AuthorityLevel.RbgHead, "RBG Head", "rbg-head", 3, true, true, 1, 1_000_000m, AuthorityScopes.Global),
        new(AuthorityLevel.ProductHead, "Product Head", "product-head", 3, true, true, 1, 1_000_000m, AuthorityScopes.Global),
        new(AuthorityLevel.CreditHead, "Credit Head", "credit-head", 3, true, true, 1, 1_000_000m, AuthorityScopes.Global),
        new(AuthorityLevel.Coo, "COO", "coo", 4, true, true, 2, 1_200_000m, AuthorityScopes.Global),
        new(AuthorityLevel.PresidentCeo, "President / CEO", "president-ceo", 5, true, true, 2, 1_500_000m, AuthorityScopes.Global),
        new(AuthorityLevel.CreComBod, "CreCom / Board", "crecom-board", 6, true, true, 2, 10_000_000m, AuthorityScopes.Global),
    ];

    public static ApprovalAuthorityDefault? For(AuthorityLevel level) =>
        All.FirstOrDefault(x => x.Level == level);

    public static string? KeyFor(AuthorityLevel level) => For(level)?.Key;

    public static ApprovalAuthority Create(ApprovalAuthorityDefault def) =>
        ApprovalAuthority.Create(
            def.DisplayName,
            def.Tier,
            0,
            def.AllowNew,
            def.AllowRenewal,
            def.MaxSeverity,
            def.MaxTotalExposure,
            def.ScopeType);
}
