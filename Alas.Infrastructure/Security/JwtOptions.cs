namespace Alas.Infrastructure.Security;

public sealed class JwtOptions
{
    public string Issuer { get; init; }
    public string Audience { get; init; }
    public string SigningKey { get; init; }
    public int AccessTokenExpirationMinutes { get; init; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}