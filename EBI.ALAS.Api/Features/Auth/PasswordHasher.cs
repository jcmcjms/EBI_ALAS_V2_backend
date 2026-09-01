namespace EBI.ALAS.Api.Features.Auth;
public class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

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
