using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class GetSlaPolicy
{
    /// <summary>Default handling SLAs (hours) per workflow stage. Overridable via
    /// appsettings "WorkflowSlaHours". Terminal stages intentionally absent —
    /// they carry no handling SLA because no one "owes" an action.</summary>
    public static readonly Dictionary<string, double> DefaultSlaHours = new()
    {
        ["ForRecommendation"] = 4,
        ["ForChecking"]       = 8,
        ["ForApproval"]       = 8,
        ["ForRevision"]       = 24,
        ["ForDisbursement"]   = 24,
    };

    public static void MapGetSlaPolicyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapGet("/sla-policy", (IConfiguration config) =>
        {
            var configured = config.GetSection("WorkflowSlaHours").Get<Dictionary<string, double>>();
            var policy = DefaultSlaHours
                .Select(kv => (kv.Key,
                    Hours: configured != null && configured.TryGetValue(kv.Key, out var v) ? v : kv.Value))
                .ToDictionary(x => x.Key, x => x.Hours);

            return Results.Ok(ApiResponse<Dictionary<string, double>>.SuccessResponse(policy));
        })
        .WithName("GetLoanSlaPolicy")
        .Produces<ApiResponse<Dictionary<string, double>>>(200)
        .RequireAuthorization("CanViewLoan");
    }
}
