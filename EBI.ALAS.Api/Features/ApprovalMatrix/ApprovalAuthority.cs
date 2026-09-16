namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public enum DeviationSeverity { None = 0, Minor = 1, Major = 2 }
public enum AuthorityScope { Branch = 0, Area = 1, Global = 2 }

/// <summary>
/// One row per approver title. Tier = delegation level; Priority =
/// availability-fallback order inside the tier (BranchHead -> OIC1 ...).
/// Seeded from the delegation-of-authority matrix.
/// </summary>
public class ApprovalAuthority
{
    public string Key { get; set; } = null!;             // PK (e.g. "BranchHead", "OICLevel1")
    public string DisplayName { get; set; } = null!;    // Human-readable (e.g. "Branch Head")
    public int Tier { get; set; }                        // Delegation level (1-5)
    public int Priority { get; set; }                    // Fallback order within tier (lower = higher priority)
    public bool AllowNew { get; set; }                   // Can approve New loans
    public bool AllowRenewal { get; set; }               // Can approve Renewal loans
    public DeviationSeverity MaxSeverity { get; set; }   // Maximum deviation severity this authority can handle
    public decimal MaxTotalExposure { get; set; }        // Maximum total exposure (proposed + outstanding)
    public AuthorityScope ScopeType { get; set; }        // Branch, Area, or Global visibility
}
