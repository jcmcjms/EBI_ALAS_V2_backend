namespace Alas.Infrastructure.Security;

public sealed record LoginRequest(
    string Username,
    string Password);

public sealed record RefreshRequest(
    string? RefreshToken);
