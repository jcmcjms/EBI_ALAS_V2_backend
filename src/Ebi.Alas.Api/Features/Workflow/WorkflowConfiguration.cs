using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.SystemSettings;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Features.Workflow;

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";
    public bool RequireRecommendation { get; set; } = true;
    public decimal MinimumNthp { get; set; } = 5_800m;
}

public interface IWorkflowConfiguration
{
    bool RequireRecommendation { get; }
    decimal MinimumNthp { get; }
    string InitialStatus { get; }
    IReadOnlyList<string> Stages { get; }
    string Source { get; }
    DateTime? UpdatedAt { get; }
    string? UpdatedByName { get; }
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);
    void Apply(SettingSnapshot snapshot);
}

public sealed class WorkflowConfiguration : IWorkflowConfiguration
{
    private readonly IOptionsMonitor<WorkflowOptions> _options;
    private readonly ISystemSettingsStore _store;
    private SettingSnapshot _override = SettingSnapshot.Empty;
    private bool _loaded;

    public WorkflowConfiguration(
        IOptionsMonitor<WorkflowOptions> options,
        ISystemSettingsStore store)
    {
        _options = options;
        _store = store;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        _override = await _store.GetSnapshotAsync(
            SystemSettingKeys.RequireRecommendation,
            cancellationToken);
        _loaded = true;
    }

    public void Apply(SettingSnapshot snapshot)
    {
        _override = snapshot;
        _loaded = true;
    }

    public bool RequireRecommendation =>
        _override.Value ?? _options.CurrentValue.RequireRecommendation;

    public decimal MinimumNthp =>
        _options.CurrentValue.MinimumNthp;

    public string InitialStatus =>
        RequireRecommendation ? "ForRecommendation" : "ForChecking";

    public IReadOnlyList<string> Stages =>
        RequireRecommendation
            ? ["ForRecommendation", "ForChecking", "ForApproval"]
            : ["ForChecking", "ForApproval"];

    public string Source => _override.Value is null ? "AppConfig" : "Database";

    public DateTime? UpdatedAt => _override.UpdatedAt;

    public string? UpdatedByName => _override.UpdatedByName;
}
