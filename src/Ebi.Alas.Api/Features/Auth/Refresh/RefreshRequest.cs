namespace Ebi.Alas.Api.Features.Auth.Refresh;

public sealed record RefreshRequest
{
    /// <summary>
    /// Opaque refresh token. Optional when the HttpOnly refresh cookie is present.
    /// </summary>
    public string? RefreshToken { get; init; }
}

