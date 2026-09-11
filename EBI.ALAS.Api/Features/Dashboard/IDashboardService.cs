namespace EBI.ALAS.Api.Features.Dashboard;

public interface IDashboardService
{
    Task<DashboardOverviewResponse> GetOverviewAsync(
        string? branchCode, string? role, CancellationToken ct = default);
}
