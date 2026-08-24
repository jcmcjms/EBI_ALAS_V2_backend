using Alas.Application.Admin.Roles;
using Alas.Application.Common.Security;
using Alas.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alas.Infrastructure.Services;

public sealed class RoleService
{
    private readonly RoleManager<AppRole> _roleManager;
    private readonly UserManager<AppUser> _userManager;

    public RoleService(
        RoleManager<AppRole> roleManager,
        UserManager<AppUser> userManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
    }

    public async Task<IReadOnlyCollection<RoleListItemDto>> ListAsync(
        CancellationToken cancellationToken)
    {
        var roles = await _roleManager.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var items = new List<RoleListItemDto>();

        foreach (var role in roles)
        {
            var userCount = await _userManager.GetUsersInRoleAsync(role.Name!);
            items.Add(new RoleListItemDto(
                role.Id,
                role.Name!,
                role.Description,
                userCount.Count));
        }

        return items;
    }

    public async Task<RoleDetailDto?> GetDetailAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());

        if (role is null)
        {
            return null;
        }

        var claims = await _roleManager.GetClaimsAsync(role);
        var permissions = claims
            .Where(c => c.Type == AlasClaimTypes.Permission)
            .Select(c => c.Value!)
            .OrderBy(p => p)
            .ToList();

        var userCount = await _userManager.GetUsersInRoleAsync(role.Name!);

        return new RoleDetailDto(
            role.Id,
            role.Name!,
            role.Description,
            permissions,
            userCount.Count);
    }

    public async Task<RoleDetailDto?> CreateAsync(
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = new AppRole
        {
            Name = request.Name.Trim(),
            NormalizedName = request.Name.Trim().ToUpperInvariant(),
            Description = request.Description?.Trim()
        };

        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create role: {errors}");
        }

        return await GetDetailAsync(role.Id, cancellationToken);
    }

    public async Task<bool> AssignPermissionsAsync(
        Guid roleId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());

        if (role is null)
        {
            return false;
        }

        var currentClaims = await _roleManager.GetClaimsAsync(role);
        var currentPermissionClaims = currentClaims
            .Where(c => c.Type == AlasClaimTypes.Permission)
            .ToList();

        foreach (var claim in currentPermissionClaims)
        {
            await _roleManager.RemoveClaimAsync(role, claim);
        }

        var validPermissions = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(AlasPermissions.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var permission in validPermissions)
        {
            await _roleManager.AddClaimAsync(role,
                new System.Security.Claims.Claim(AlasClaimTypes.Permission, permission));
        }

        return true;
    }
}
