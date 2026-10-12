using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.SystemSettings;

public class SystemSetting
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedById { get; set; }
    public User UpdatedBy { get; set; } = null!;
}

public sealed record SettingSnapshot(bool? Value, DateTime? UpdatedAt, string? UpdatedByName)
{
    public static readonly SettingSnapshot Empty = new(null, null, null);
}

public interface ISystemSettingsStore
{
    Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default);
    Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Reads SystemSettings directly from the database. No process-local cache:
/// values must be consistent across instances and this store is on a low-volume
/// admin path (workflow configuration), so a stale 30s IMemoryCache was a
/// multi-instance correctness risk with no meaningful load benefit.
/// </summary>
public sealed class SystemSettingsStore : ISystemSettingsStore
{
    private readonly AlasDbContext _db;
    private readonly TimeProvider _time;

    public SystemSettingsStore(AlasDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => new { s.Value, s.UpdatedAt, Name = s.UpdatedBy.FirstName + " " + s.UpdatedBy.LastName })
            .FirstOrDefaultAsync(ct);

        return row is null || !bool.TryParse(row.Value, out var parsed)
            ? SettingSnapshot.Empty
            : new SettingSnapshot(parsed, row.UpdatedAt, row.Name);
    }

    public async Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
    {
        var row = await _db.SystemSettings.FindAsync(new object[] { key }, ct);
        if (row is null)
        {
            row = new SystemSetting { Key = key };
            _db.SystemSettings.Add(row);
        }
        row.Value = value ? "true" : "false";
        row.UpdatedAt = _time.GetUtcNow().DateTime;
        row.UpdatedById = userId;
        await _db.SaveChangesAsync(ct);

        var name = await _db.Users.Where(u => u.Id == userId)
            .Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? string.Empty;

        return new SettingSnapshot(value, row.UpdatedAt, name);
    }
}

public static class SystemSettingKeys
{
    public const string RequireRecommendation = "Workflow.RequireRecommendation";
}
