using Microsoft.AspNetCore.Identity;

namespace Alas.Infrastructure.Identity;

public class AppRole : IdentityRole<Guid>
{
    public string Description { get; set; }
}