namespace Ebi.Alas.Api.Features.Auth.Login;

public sealed record LoginRequest
{
    public required string UserName { get; init; }

    public required string Password { get; init; }
}

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    bool MustChangePassword);

public abstract record LoginOutcome
{
    public sealed record Success(LoginResponse Response) : LoginOutcome;

    public sealed record Failure(string Detail, int StatusCode) : LoginOutcome;

    public static LoginOutcome InvalidCredentials() =>
        new Failure("Invalid username or password.", StatusCodes.Status401Unauthorized);

    public static LoginOutcome Suspended() =>
        new Failure("Account is suspended.", StatusCodes.Status403Forbidden);
}
