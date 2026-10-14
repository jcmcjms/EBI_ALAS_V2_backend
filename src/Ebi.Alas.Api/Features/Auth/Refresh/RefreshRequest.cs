namespace Ebi.Alas.Api.Features.Auth.Refresh;

public sealed record RefreshRequest
{
    public string? RefreshToken { get; init; }
}

