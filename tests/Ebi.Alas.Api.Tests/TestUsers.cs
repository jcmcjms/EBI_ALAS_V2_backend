using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;

namespace Ebi.Alas.Api.Tests;

/// <summary>Shared helpers for signature-aware tests.</summary>
internal static class TestUsers
{
    /// <summary>Creates a user with a valid sanitized PNG e-signature.</summary>
    public static User WithSignature(User user, DateTimeOffset? now = null)
    {
        user.SetSignature(
            Features.Users.SignatureTestImages.CreatePngDataUrl(),
            now ?? DateTimeOffset.UtcNow);
        return user;
    }

    /// <summary>Seeds a signed encoder and returns a matching caller context.</summary>
    public static async Task<CallerContext> SeedSignedCallerAsync(
        AlasDbContext db,
        UserRole role = UserRole.Encoder,
        string branchId = "BR-A",
        string? jobTitle = null)
    {
        var now = DateTimeOffset.UtcNow;
        var user = WithSignature(User.Create(
            $"u{Guid.NewGuid():N}"[..12],
            "hash",
            "Test",
            "User",
            "",
            $"t{Guid.NewGuid():N}@x.com",
            branchId,
            role,
            now,
            jobTitle));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new CallerContext(user.Id, role, branchId);
    }
}
