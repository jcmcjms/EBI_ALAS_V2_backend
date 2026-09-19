namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Login request payload. Immutable record — all properties use init setters.
/// </summary>
public sealed record LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Login response payload. Immutable record — all properties use init setters.
/// </summary>
public sealed record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}

/// <summary>
/// Change password request payload. Positional record for immutability.
/// </summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
