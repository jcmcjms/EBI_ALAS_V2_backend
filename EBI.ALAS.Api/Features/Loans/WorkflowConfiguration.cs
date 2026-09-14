using EBI.ALAS.Api.Common.Models;
using Microsoft.Extensions.Options;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Workflow feature flags. Bound from appsettings "Workflow" section (or env
/// var Workflow__RequireRecommendation=false in containers). IOptionsMonitor
/// means the file is watched: flipping the value takes effect on the next
/// request WITHOUT a redeploy or restart.
/// </summary>
public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    /// <summary>
    /// true  = Draft → ForRecommendation → ForChecking → ForApproval (classic).
    /// false = Draft → ForChecking → ForApproval (recommender step skipped).
    /// Loans ALREADY in ForRecommendation remain actionable either way.
    /// </summary>
    public bool RequireRecommendation { get; set; } = true;
}

public interface IWorkflowConfiguration
{
    bool RequireRecommendation { get; }

    /// <summary>Status a brand-new submission lands in.</summary>
    string InitialStatus { get; }

    /// <summary>Ordered review stages after submission (for UI steppers).</summary>
    IReadOnlyList<string> Stages { get; }
}

public sealed class WorkflowConfiguration : IWorkflowConfiguration
{
    private readonly IOptionsMonitor<WorkflowOptions> _options;

    public WorkflowConfiguration(IOptionsMonitor<WorkflowOptions> options) => _options = options;

    public bool RequireRecommendation => _options.CurrentValue.RequireRecommendation;

    public string InitialStatus =>
        RequireRecommendation ? "ForRecommendation" : "ForChecking";

    public IReadOnlyList<string> Stages =>
        RequireRecommendation
            ? ["ForRecommendation", "ForChecking", "ForApproval"]
            : ["ForChecking", "ForApproval"];
}

public static class WorkflowConfigurationEndpoints
{
    /// <summary>
    /// GET /api/workflow/configuration — the single source of truth the UI
    /// reads so it never hardcodes the pipeline shape.
    /// </summary>
    public static void MapWorkflowConfigurationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/workflow/configuration", (IWorkflowConfiguration cfg) =>
            Results.Ok(ApiResponse<WorkflowConfigurationResponse>.SuccessResponse(new(
                cfg.RequireRecommendation, cfg.InitialStatus, cfg.Stages))))
        .WithName("GetWorkflowConfiguration")
        .Produces<ApiResponse<WorkflowConfigurationResponse>>(200)
        .RequireAuthorization();   // any authenticated officer; no permission tier needed
    }
}

public sealed record WorkflowConfigurationResponse(
    bool RequireRecommendation,
    string InitialStatus,
    IReadOnlyList<string> Stages);
