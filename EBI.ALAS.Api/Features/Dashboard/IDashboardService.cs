namespace EBI.ALAS.Api.Features.Dashboard;
public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(string? branchId = null, string? role = null);
}
