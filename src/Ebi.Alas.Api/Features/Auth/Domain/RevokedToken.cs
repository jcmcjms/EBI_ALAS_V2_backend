namespace Ebi.Alas.Api.Features.Auth.Domain;

public sealed class RevokedToken
{
    private RevokedToken()
    {
        Jti = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Jti { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset RevokedAt { get; private set; }

    public static RevokedToken Create(string jti, Guid userId, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jti);
        return new RevokedToken
        {
            Id = Guid.NewGuid(),
            Jti = jti,
            UserId = userId,
            ExpiresAt = expiresAt,
            RevokedAt = now
        };
    }
}
