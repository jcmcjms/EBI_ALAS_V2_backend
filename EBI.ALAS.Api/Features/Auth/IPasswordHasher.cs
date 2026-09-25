namespace EBI.ALAS.Api.Features.Auth;
public interface IPasswordHasher
{
    /// <summary>User-chosen secrets: low entropy, lifetime of the account.</summary>
    const int InteractiveWorkFactor = 14;

    /// <summary>
    /// Server-generated one-time credentials: ~70 bits of CSPRNG entropy,
    /// shown once, must-change, and time-boxed via TempPasswordExpiresAt.
    /// Cost 12 still exceeds OWASP's BCrypt floor.
    /// </summary>
    const int TemporaryWorkFactor = 12;

    string HashPassword(string password, int workFactor = InteractiveWorkFactor);
    bool VerifyPassword(string password, string hashedPassword);
}
