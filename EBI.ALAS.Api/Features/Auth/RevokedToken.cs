namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Stores revoked JWT token IDs (jti) to enforce logout before natural expiry.
/// Entries are cleaned up after the token's original expiry time.
/// </summary>
public class RevokedToken
{
    public int Id { get; set; }

    public string TokenId { get; set; } = string.Empty;

    public int UserId { get; set; }

    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }
}
