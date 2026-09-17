using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Features.SystemSettings;

/// <summary>
/// Generic key/value system settings (workflow flags, thresholds…).
/// Value is stored as string so the table never needs schema changes.
/// </summary>
public class SystemSetting
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public int UpdatedById { get; set; }
    public User UpdatedBy { get; set; } = null!;
}

/// <summary>
/// null Value = no database override; fall back to appsettings.
/// </summary>
public sealed record SettingSnapshot(bool? Value, DateTime? UpdatedAt, string? UpdatedByName)
{
    public static readonly SettingSnapshot Empty = new(null, null, null);
}

public interface ISystemSettingsStore
{
    Task<SettingSnapshot> GetSnapshotAsync(string key, bool bypassCache = false, CancellationToken ct = default);
    Task<SettingSnapshot> SetBoolAsync(string key, bool value, int userId, CancellationToken ct = default);
}

public sealed class SystemSettingsStore : ISystemSettingsStore
{
    private const string CachePrefix = "syssetting:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly Common.Time.ITimeProvider _time;

    public SystemSettingsStore(AppDbContext db, IMemoryCache cache, Common.Time.ITimeProvider time)
    {
        _db = db;
        _cache = cache;
        _time = time;
    }

    public async Task<SettingSnapshot> GetSnapshotAsync(string key, bool bypassCache = false, CancellationToken ct = default)
    {
        var cacheKey = CachePrefix + key;
        if (!bypassCache && _cache.TryGetValue(cacheKey, out SettingSnapshot? cached) && cached is not null)
            return cached;

        var row = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => new { s.Value, s.UpdatedAt, Name = s.UpdatedBy.FirstName + " " + s.UpdatedBy.LastName })
            .FirstOrDefaultAsync(ct);

        var snapshot = row is null || !bool.TryParse(row.Value, out var parsed)
            ? SettingSnapshot.Empty
            : new SettingSnapshot(parsed, row.UpdatedAt, row.Name);

        _cache.Set(cacheKey, snapshot, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = CacheTtl,
        });
        return snapshot;
    }

    public async Task<SettingSnapshot> SetBoolAsync(string key, bool value, int userId, CancellationToken ct = default)
    {
        var row = await _db.SystemSettings.FindAsync(new object[] { key }, ct);
        if (row is null)
        {
            row = new SystemSetting { Key = key };
            _db.SystemSettings.Add(row);
        }

        row.Value = value ? "true" : "false";
        row.UpdatedAt = _time.UtcNow;
        row.UpdatedById = userId;
        await _db.SaveChangesAsync(ct);

        var name = await _db.Users.Where(u => u.Id == userId)
            .Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? string.Empty;

        var snapshot = new SettingSnapshot(value, row.UpdatedAt, name);
        _cache.Set(CachePrefix + key, snapshot, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = CacheTtl,
        });
        return snapshot;
    }
}

public static class SystemSettingKeys
{
    public const string RequireRecommendation = "Workflow.RequireRecommendation";
}
