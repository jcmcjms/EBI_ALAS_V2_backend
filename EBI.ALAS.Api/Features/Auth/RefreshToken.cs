namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Stores refresh tokens for session persistence via HttpOnly cookies.
/// Each refresh token is single-use: rotation revokes the old and issues a new one.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>
    /// SHA-256 hash of the refresh token string. The raw token is never stored.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The user who owns this refresh token.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Navigation property to the owning user.
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Optional device/client identifier for auditing.
    /// </summary>
    public string? DeviceInfo { get; set; }

    /// <summary>
    /// When this refresh token was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this refresh token expires (sliding window).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Absolute session deadline — forces re-login regardless of refresh activity.
    /// </summary>
    public DateTime AbsoluteExpiry { get; set; }

    /// <summary>
    /// Whether this token has been revoked (rotation or logout).
    /// </summary>
    public bool IsRevoked { get; set; } = false;

    /// <summary>
    /// When the token was revoked (null if still active).
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}
