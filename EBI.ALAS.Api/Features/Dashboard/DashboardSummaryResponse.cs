namespace EBI.ALAS.Api.Features.Dashboard;

/// <summary>
/// Dashboard summary response DTO with aggregated loan statistics.
/// </summary>
public class DashboardSummaryResponse
{
    public int TotalApplications { get; set; }
    public decimal TotalAmount { get; set; }
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public Dictionary<string, BranchSummary> BranchCounts { get; set; } = new();
}

/// <summary>
/// Branch-level summary for the dashboard.
/// </summary>
public class BranchSummary
{
    public string BranchCode { get; set; } = string.Empty;
    public int ApplicationCount { get; set; }
    public decimal TotalAmount { get; set; }
}
