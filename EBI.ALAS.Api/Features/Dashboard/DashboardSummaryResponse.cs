namespace EBI.ALAS.Api.Features.Dashboard;
public class DashboardSummaryResponse
{
    public int TotalApplications { get; set; }
    public decimal TotalAmount { get; set; }
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public Dictionary<string, BranchSummary> BranchCounts { get; set; } = new();
}
public class BranchSummary
{
    public string BranchCode { get; set; } = string.Empty;
    public int ApplicationCount { get; set; }
    public decimal TotalAmount { get; set; }
}
