namespace EBI.ALAS.Api.Features.ApprovalMatrix;
public enum DeviationSeverity { None = 0, Minor = 1, Major = 2 }
public enum AuthorityScope { Branch = 0, Area = 1, Global = 2 }
public class ApprovalAuthority
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public int Tier { get; set; }
    public int Priority { get; set; }
    public bool AllowNew { get; set; }
    public bool AllowRenewal { get; set; }
    public DeviationSeverity MaxSeverity { get; set; }
    public decimal MaxTotalExposure { get; set; }
    public AuthorityScope ScopeType { get; set; }
}
