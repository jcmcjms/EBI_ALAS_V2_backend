using Ebi.Alas.Api.Features.Auth.Login;

namespace Ebi.Alas.Api.Features.Auth.Refresh;

public abstract record RefreshOutcome
{
    public sealed record Success(LoginResponse Response) : RefreshOutcome;

    public sealed record Failure(string Detail, int StatusCode) : RefreshOutcome;

    public static RefreshOutcome InvalidToken() =>
        new Failure("Refresh token is invalid or expired.", StatusCodes.Status401Unauthorized);

    public static RefreshOutcome Suspended() =>
        new Failure("Account is suspended.", StatusCodes.Status403Forbidden);
}
