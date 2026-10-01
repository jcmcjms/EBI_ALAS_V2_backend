namespace EBI.ALAS.Api.Features.Auth;
public sealed record LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
public sealed record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
