using Alas.Application.Common.Security;
using Alas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Alas.Infrastructure.Security;

public class UserPermissionProvider: IUserPermissionProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly AlasDbContext _context;
    private readonly IMemoryCache _cache;

    public UserPermissionProvider(AlasDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<UserPermissionSet> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(userId);

        if (_cache.TryGetValue(cacheKey, out UserPermissionSet? cached) && cached is not null)
        {
            return cached;
        }

        var user = await _context.Users
            .AsNoTracking()
            .Where(e => e.Id == userId)
            .Select(e => new
            {
                e.IsActive,
                e.PermissionVersion
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return new UserPermissionSet(userId, false, 0, new HashSet<string>(StringComparer.Ordinal));
        }

        var permissions = await _context.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Join(
                _context.RoleClaims,
                userRole => userRole.RoleId,
                roleClaim => roleClaim.RoleId,
                (userRole, roleClaim) => roleClaim)
            .Where(roleClaim => roleClaim.ClaimType == AlasClaimTypes.Permission)
            .Select(roleClaim => roleClaim.ClaimValue!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var permissionSet = new UserPermissionSet(
            userId,
            user.IsActive,
            user.PermissionVersion,
            permissions.ToHashSet(StringComparer.Ordinal));

        _cache.Set(cacheKey, permissionSet, CacheDuration);
        return permissionSet;
    }

    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken)
    {
        _cache.Remove(BuildCacheKey(userId));
        return Task.CompletedTask;
    }

    private static string BuildCacheKey(Guid userId)
    {
        return $"user-permissions:{userId}";
    }
}