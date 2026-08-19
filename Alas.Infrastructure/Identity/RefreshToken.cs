namespace Alas.Infrastructure.Identity;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string RevokedReason { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public bool IsRevoked => RevokedUtc.HasValue;
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresUtc;
    public bool IsActive => !IsRevoked && !IsExpired;

}