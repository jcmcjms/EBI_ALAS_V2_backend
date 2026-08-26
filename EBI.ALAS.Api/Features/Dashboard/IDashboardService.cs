namespace EBI.ALAS.Api.Features.Dashboard;

/// <summary>
/// Interface for dashboard data aggregation services.
/// </summary>
public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(string? branchId = null, string? role = null);
}
