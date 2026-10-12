using Ebi.Alas.Api.Features.Auth.Login;

namespace Ebi.Alas.Api.Features.Auth.Refresh;

public abstract record RefreshOutcome
{
    /// <param name="RawRefreshToken">
    /// Opaque refresh token for the HttpOnly cookie only; never serialized to clients.
    /// </param>
    public sealed record Success(LoginResponse Response, string RawRefreshToken) : RefreshOutcome;

    public sealed record Failure(string Detail, int StatusCode) : RefreshOutcome;

    public static RefreshOutcome InvalidToken() =>
        new Failure("Refresh token is invalid or expired.", StatusCodes.Status401Unauthorized);

    public static RefreshOutcome Suspended() =>
        new Failure("Account is suspended.", StatusCodes.Status403Forbidden);
}
