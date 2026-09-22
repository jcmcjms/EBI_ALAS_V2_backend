namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public class ApprovalAuthorityDto
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Tier { get; set; }
    public int Priority { get; set; }
    public bool AllowNew { get; set; }
    public bool AllowRenewal { get; set; }
    public int MaxSeverity { get; set; }
    public decimal MaxTotalExposure { get; set; }
    public int ScopeType { get; set; }
}

public class DeviationCatalogDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Severity { get; set; }
}

public class ApproverPresenceDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AuthorityKey { get; set; } = string.Empty;
    public int Tier { get; set; }
    public bool Online { get; set; }
    public bool Reviewing { get; set; }
}
