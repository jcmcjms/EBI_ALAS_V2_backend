namespace Ebi.Alas.Api.Features.Auth.Login;

public sealed record LoginRequest
{
    public required string UserName { get; init; }

    public required string Password { get; init; }
}

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    bool MustChangePassword);

public abstract record LoginOutcome
{
    /// <param name="RawRefreshToken">
    /// Opaque refresh token for the HttpOnly cookie only; never serialized to clients.
    /// </param>
    public sealed record Success(LoginResponse Response, string RawRefreshToken) : LoginOutcome;

    public sealed record Failure(string Detail, int StatusCode) : LoginOutcome;

    public static LoginOutcome InvalidCredentials() =>
        new Failure("Invalid username or password.", StatusCodes.Status401Unauthorized);

    public static LoginOutcome Suspended() =>
        new Failure("Account is suspended.", StatusCodes.Status403Forbidden);
}
