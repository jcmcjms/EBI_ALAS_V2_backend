using Microsoft.AspNetCore.Identity;

namespace Alas.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }
    public bool IsActive { get; set; }
    public int PermissionVersion { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}