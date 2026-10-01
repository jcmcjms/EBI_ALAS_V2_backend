using EBI.ALAS.Api.Features.ApprovalMatrix;
namespace EBI.ALAS.Api.Features.Auth;
public sealed class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? EmergencyContact { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? JobTitle { get; set; }
    public string? ESignature { get; set; }
    public string? ApprovalAuthorityKey { get; set; }
    public ApprovalAuthority? ApprovalAuthority { get; set; }
    public DateTime? TempPasswordExpiresAt { get; set; }
    public ICollection<UserBranchCoverage> BranchCoverages { get; set; } = new List<UserBranchCoverage>();
}
