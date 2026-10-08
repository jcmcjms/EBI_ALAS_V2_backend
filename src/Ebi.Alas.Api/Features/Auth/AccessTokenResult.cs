namespace Ebi.Alas.Api.Features.Auth;

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt, string Jti);
