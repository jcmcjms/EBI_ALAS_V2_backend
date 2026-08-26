namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Stores revoked JWT token IDs (jti) to enforce logout before natural expiry.
/// Entries are cleaned up after the token's original expiry time.
/// </summary>
public class RevokedToken
{
    public int Id { get; set; }

    /// <summary>
    /// The JWT ID (jti claim) of the revoked token.
    /// </summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the user who owned the token.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// When the token was revoked.
    /// </summary>
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the original token expires (for cleanup).
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
