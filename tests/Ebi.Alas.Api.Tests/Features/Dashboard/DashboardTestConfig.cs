using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Workflow;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.Dashboard;

internal static class DashboardTestConfig
{
    public static IWorkflowConfiguration Config(bool requireRecommendation = true)
    {
        var store = new FakeSettingsStore(
            new SettingSnapshot(requireRecommendation, DateTime.UtcNow, "Admin"));
        return new WorkflowConfiguration(
            new StaticOptionsMonitor(new WorkflowOptions()),
            store);
    }

    private sealed class StaticOptionsMonitor(WorkflowOptions value) : IOptionsMonitor<WorkflowOptions>
    {
        public WorkflowOptions CurrentValue => value;
        public WorkflowOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<WorkflowOptions, string?> listener) => null;
    }

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }
}
