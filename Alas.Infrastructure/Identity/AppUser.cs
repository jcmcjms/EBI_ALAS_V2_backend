using Microsoft.AspNetCore.Identity;

namespace Alas.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }

    public string? BranchId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Increment this when permissions/roles change if you later want
    /// to invalidate access tokens more aggressively.
    /// </summary>
    public int PermissionVersion { get; set; } = 1;

    public DateTimeOffset CreatedUtc { get; set; }
}
