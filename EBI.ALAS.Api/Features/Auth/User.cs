using EBI.ALAS.Api.Features.ApprovalMatrix;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// User entity. Represents a system user with authentication and profile data.
/// Navigation properties are nullable to support lazy loading.
/// </summary>
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

    // Profile fields for My Account page
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? EmergencyContact { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public DateTime? PasswordChangedAt { get; set; }

    // Workflow audit context (JobTitle) and document signing (ESignature)
    public string? JobTitle { get; set; }
    public string? ESignature { get; set; }

    // ── Delegation-of-authority routing ──────────────────────────────
    /// <summary>FK to ApprovalAuthorities.Key. Null for non-approvers.</summary>
    public string? ApprovalAuthorityKey { get; set; }

    /// <summary>Navigation property to the approval authority row.</summary>
    public ApprovalAuthority? ApprovalAuthority { get; set; }

    /// <summary>Multi-branch coverage for Branch-scope approvers.</summary>
    public ICollection<UserBranchCoverage> BranchCoverages { get; set; } = new List<UserBranchCoverage>();
}
