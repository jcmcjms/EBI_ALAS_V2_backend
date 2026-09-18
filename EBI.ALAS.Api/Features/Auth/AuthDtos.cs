namespace EBI.ALAS.Api.Features.Auth;

public record LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
