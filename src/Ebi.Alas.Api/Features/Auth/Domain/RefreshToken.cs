namespace Ebi.Alas.Api.Features.Auth.Domain;

public sealed class RefreshToken
{
    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        TimeSpan absoluteLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = now.Add(lifetime),
            AbsoluteExpiresAt = now.Add(absoluteLifetime),
            CreatedAt = now
        };
    }

    public bool IsUsable(DateTimeOffset now) =>
        RevokedAt is null && now < ExpiresAt && now < AbsoluteExpiresAt;

    public void Revoke(DateTimeOffset now, Guid replacedById)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        ReplacedById = replacedById;
    }
}
