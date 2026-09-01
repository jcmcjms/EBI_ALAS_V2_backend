namespace EBI.ALAS.Api.Features.Auth;
public class RevokedToken
{
    public int Id { get; set; }

    public string TokenId { get; set; } = string.Empty;

    public int UserId { get; set; }

    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }
}
