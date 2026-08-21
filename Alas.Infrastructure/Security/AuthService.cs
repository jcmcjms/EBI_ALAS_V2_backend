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

    public async Task<TokenResponse?> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(request.Username);

        if (user is null)
        {
            return null;
        }

        if (!user.IsActive)
        {
            return null;
        }

        if (await _userManager.IsEmailConfirmedAsync(user))
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

        var permissionSet = await _permissionProvider.GetAsync(user.Id, cancellationToken);

        if (!permissionSet.IsActive)
        {
            return null;
        }

        var accessToken = _tokenService.CreateAccessToken(user, permissionSet.PermissionVersion);

        var refreshToken = await _refreshTokenService.CreateAsync(
            user.Id,
            ipAddress,
            userAgent, cancellationToken);

        return new TokenResponse(
            accessToken,
            refreshToken,
            _jwtOptions.AccessTokenExpirationMinutes * 60);
    }

    public async Task<TokenResponse?> RefreshAsync(
        RefreshRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var storedToken = await _refreshTokenService.FindByTokenAsync(
            request.RefreshToken,
            cancellationToken);

        if (storedToken is null)
        {
            return null;
        }

        if (storedToken.RevokedUtc.HasValue)
        {
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

        var permissionSet = await _permissionProvider.GetAsync(user.Id, cancellationToken);

        if (!permissionSet.IsActive)
        {
            return null;
        }

        var accessToken = _tokenService.CreateAccessToken(user, permissionSet.PermissionVersion);

        var newRefreshToken = await _refreshTokenService.RotateAsync(
            storedToken,
            ipAddress, userAgent, cancellationToken);

        return new TokenResponse(accessToken, newRefreshToken, _jwtOptions.AccessTokenExpirationMinutes * 60);
    }
    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var storedToken = await _refreshTokenService.FindByTokenAsync(
            refreshToken, cancellationToken);
        if (storedToken is null || storedToken.RevokedUtc.HasValue)
        {
            return;
        }

        await _refreshTokenService.RevokeAsync(storedToken, "Logged out", cancellationToken);
    }
}