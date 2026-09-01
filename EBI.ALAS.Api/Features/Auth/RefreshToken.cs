namespace EBI.ALAS.Api.Features.Auth;
public class RefreshToken
{
    public int Id { get; set; }

    // SHA-256 hash; the raw token is never persisted.
    public string TokenHash { get; set; } = string.Empty;

    public int UserId { get; set; }

    public User? User { get; set; }

    public string? DeviceInfo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Sliding window — reset on every successful rotation.
    public DateTime ExpiresAt { get; set; }

    // Hard cap — forces re-login regardless of refresh activity.
    public DateTime AbsoluteExpiry { get; set; }

    public bool IsRevoked { get; set; } = false;

    public DateTime? RevokedAt { get; set; }
}
