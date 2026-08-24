using Alas.Application.Common.Security;
using Alas.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Alas.Infrastructure.Security;

public sealed class AuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RefreshTokenService _refreshTokenService;
    private readonly TokenService _tokenService;
    private readonly IUserPermissionProvider _permissionProvider;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        UserManager<AppUser> userManager,
        RefreshTokenService refreshTokenService,
        TokenService tokenService,
        IUserPermissionProvider permissionProvider,
        IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _refreshTokenService = refreshTokenService;
        _tokenService = tokenService;
        _permissionProvider = permissionProvider;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<AuthResult?> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(request.Username);

        if (user is null)
        {
            return null;
        }

        if (!user.IsActive)
        {
            return null;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);

        if (!passwordValid)
        {
            await _userManager.AccessFailedAsync(user);
            return null;
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var permissionSet = await _permissionProvider.GetAsync(
            user.Id,
            cancellationToken);

        if (!permissionSet.IsActive)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);

        var accessToken = _tokenService.CreateAccessToken(
            user,
            permissionSet.PermissionVersion,
            roles);

        var refreshToken = await _refreshTokenService.CreateAsync(
            user.Id,
            ipAddress,
            userAgent,
            cancellationToken);

        var userDto = BuildUserDto(user, roles, permissionSet.Permissions);

        return new AuthResult(
            accessToken,
            refreshToken,
            _jwtOptions.AccessTokenExpirationMinutes * 60,
            _jwtOptions.RefreshTokenExpirationDays,
            userDto);
    }

    public async Task<AuthResult?> RefreshAsync(
        string refreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var storedToken = await _refreshTokenService.FindByTokenAsync(
            refreshToken,
            cancellationToken);

        if (storedToken is null)
        {
            return null;
        }

        if (storedToken.RevokedUtc.HasValue)
        {
            // Possible refresh token reuse — revoke all sessions for this user.
            await _refreshTokenService.RevokedAllForUserAsync(
                storedToken.UserId,
                "Refresh token reuse detected",
                cancellationToken);

            return null;
        }

        if (storedToken.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(storedToken.UserId.ToString());

        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        var permissionSet = await _permissionProvider.GetAsync(
            user.Id,
            cancellationToken);

        if (!permissionSet.IsActive)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);

        var accessToken = _tokenService.CreateAccessToken(
            user,
            permissionSet.PermissionVersion,
            roles);

        var newRefreshToken = await _refreshTokenService.RotateAsync(
            storedToken,
            ipAddress,
            userAgent,
            cancellationToken);

        var userDto = BuildUserDto(user, roles, permissionSet.Permissions);

        return new AuthResult(
            accessToken,
            newRefreshToken,
            _jwtOptions.AccessTokenExpirationMinutes * 60,
            _jwtOptions.RefreshTokenExpirationDays,
            userDto);
    }

    public async Task LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var storedToken = await _refreshTokenService.FindByTokenAsync(
            refreshToken,
            cancellationToken);

        if (storedToken is null || storedToken.RevokedUtc.HasValue)
        {
            return;
        }

        await _refreshTokenService.RevokeAsync(
            storedToken,
            "Logged out",
            cancellationToken);
    }

    public async Task<AuthUserDto?> GetSessionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null || !user.IsActive)
        {
            return null;
        }

        var permissionSet = await _permissionProvider.GetAsync(
            user.Id,
            cancellationToken);

        if (!permissionSet.IsActive)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);

        return BuildUserDto(user, roles, permissionSet.Permissions);
    }

    private async Task<AppUser?> FindUserAsync(string usernameOrEmail)
    {
        var normalized = usernameOrEmail.Trim();

        var byUsername = await _userManager.FindByNameAsync(normalized);

        if (byUsername is not null)
        {
            return byUsername;
        }

        if (normalized.Contains('@'))
        {
            return await _userManager.FindByEmailAsync(normalized);
        }

        return null;
    }

    private static AuthUserDto BuildUserDto(
        AppUser user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var roleList = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r)
            .ToList();

        var permissionSet = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(AlasPermissions.Normalize)
            .ToHashSet(StringComparer.Ordinal);

        // Temporary compatibility for legacy frontend strings.
        // Remove after frontend migration to canonical permissions.
        if (!permissionSet.Contains(AlasPermissions.SuperAdmin))
        {
            if (permissionSet.Contains(AlasPermissions.UsersManage))
            {
                permissionSet.Add("user.create");
            }

            if (permissionSet.Contains(AlasPermissions.RolesManage))
            {
                permissionSet.Add("role.manage");
            }

            if (permissionSet.Contains(AlasPermissions.LoansRead))
            {
                permissionSet.Add("loans.view");
            }
        }

        return new AuthUserDto(
            user.Id.ToString(),
            user.UserName ?? string.Empty,
            user.FullName ?? user.UserName ?? string.Empty,
            user.BranchId ?? string.Empty,
            roleList,
            permissionSet
                .OrderBy(p => p)
                .ToList());
    }
}
