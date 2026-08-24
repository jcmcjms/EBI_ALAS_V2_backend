using Alas.Application.Admin.Users;
using Alas.Application.Common.Security;
using Alas.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alas.Infrastructure.Services;

public sealed class UserService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<AppRole> _roleManager;

    public UserService(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<PagedResult<UserListItemDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = _userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                u.UserName!.Contains(term) ||
                (u.FullName != null && u.FullName.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var users = await query
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<UserListItemDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            items.Add(new UserListItemDto(
                user.Id,
                user.UserName!,
                user.FullName ?? user.UserName!,
                user.Email,
                user.BranchId,
                user.IsActive,
                roles.ToList(),
                user.CreatedUtc));
        }

        return new PagedResult<UserListItemDto>(
            items, totalCount, page, pageSize);
    }

    public async Task<UserDetailDto?> GetDetailAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var roleClaims = new List<string>();

        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;

            var claims = await _roleManager.GetClaimsAsync(role);
            var permissions = claims
                .Where(c => c.Type == AlasClaimTypes.Permission)
                .Select(c => c.Value!)
                .ToList();

            roleClaims.AddRange(permissions);
        }

        var allPermissions = roleClaims
            .Distinct()
            .Select(AlasPermissions.Normalize)
            .OrderBy(p => p)
            .ToList();

        return new UserDetailDto(
            user.Id,
            user.UserName!,
            user.FullName ?? user.UserName!,
            user.Email,
            user.BranchId,
            user.IsActive,
            user.PermissionVersion,
            roles.ToList(),
            allPermissions,
            user.CreatedUtc);
    }

    public async Task<UserDetailDto?> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = new AppUser
        {
            UserName = request.Username.Trim(),
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim(),
            BranchId = request.BranchId?.Trim(),
            IsActive = true,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create user: {errors}");
        }

        return await GetDetailAsync(user.Id, cancellationToken);
    }

    public async Task<bool> UpdateStatusAsync(
        Guid userId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return false;
        }

        user.IsActive = isActive;
        var result = await _userManager.UpdateAsync(user);

        return result.Succeeded;
    }

    public async Task<bool> AssignRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return false;
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);

        var validRoles = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validRoles.Count > 0)
        {
            var result = await _userManager.AddToRolesAsync(user, validRoles);
            if (!result.Succeeded)
            {
                return false;
            }
        }

        user.PermissionVersion++;
        await _userManager.UpdateAsync(user);

        return true;
    }
}
