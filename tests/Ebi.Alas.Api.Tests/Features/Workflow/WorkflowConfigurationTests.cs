using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Workflow;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

public sealed class WorkflowConfigurationTests
{
    private sealed class FakeStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public int GetCalls { get; private set; }

        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
        {
            GetCalls++;
            return Task.FromResult(snapshot);
        }

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }

    private sealed class StaticOptionsMonitor(WorkflowOptions value) : IOptionsMonitor<WorkflowOptions>
    {
        public WorkflowOptions CurrentValue => value;
        public WorkflowOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<WorkflowOptions, string?> listener) => null;
    }

    [Fact]
    public async Task EnsureLoadedAsync_ReadsDatabaseOverride()
    {
        var store = new FakeStore(new SettingSnapshot(false, DateTime.UtcNow, "Ops"));
        var cfg = new WorkflowConfiguration(
            new StaticOptionsMonitor(new WorkflowOptions { RequireRecommendation = true }),
            store);

        await cfg.EnsureLoadedAsync();

        Assert.False(cfg.RequireRecommendation);
        Assert.Equal("Database", cfg.Source);
        Assert.Equal("ForChecking", cfg.InitialStatus);
        Assert.Equal("Ops", cfg.UpdatedByName);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WithoutRow_FallsBackToAppConfig()
    {
        var store = new FakeStore(SettingSnapshot.Empty);
        var cfg = new WorkflowConfiguration(
            new StaticOptionsMonitor(new WorkflowOptions { RequireRecommendation = true }),
            store);

        await cfg.EnsureLoadedAsync();

        Assert.True(cfg.RequireRecommendation);
        Assert.Equal("AppConfig", cfg.Source);
        Assert.Equal("ForRecommendation", cfg.InitialStatus);
    }

    [Fact]
    public async Task EnsureLoadedAsync_IsIdempotent()
    {
        var store = new FakeStore(new SettingSnapshot(false, DateTime.UtcNow, "Ops"));
        var cfg = new WorkflowConfiguration(
            new StaticOptionsMonitor(new WorkflowOptions()),
            store);

        await cfg.EnsureLoadedAsync();
        await cfg.EnsureLoadedAsync();

        Assert.Equal(1, store.GetCalls);
    }

    [Fact]
    public void Apply_SupersedesLoadedValue()
    {
        var store = new FakeStore(SettingSnapshot.Empty);
        var cfg = new WorkflowConfiguration(
            new StaticOptionsMonitor(new WorkflowOptions { RequireRecommendation = true }),
            store);

        cfg.Apply(new SettingSnapshot(false, DateTime.UtcNow, "Ops"));

        Assert.False(cfg.RequireRecommendation);
        Assert.Equal("Database", cfg.Source);
    }
}
