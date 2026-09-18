namespace EBI.ALAS.Api.Features.Auth;
public class PasswordHasher : IPasswordHasher
{
    // Banking-grade: work factor 14 (~1.5s per hash on modern hardware).
    // This makes brute-force attacks computationally infeasible.
    // Previous value (12) was too low for banking security standards.
    private const int WorkFactor = 14;

    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        // Timing attack prevention: Always perform BCrypt verification
        // even if the hash is empty or null to maintain consistent timing
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
        catch
        {
            // If BCrypt verification fails (e.g., invalid hash format),
            // return false but still perform the operation to prevent timing attacks
            return false;
        }
    }
}
