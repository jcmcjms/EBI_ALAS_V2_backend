namespace EBI.ALAS.Api.Features.Auth;
public class PasswordHasher : IPasswordHasher
{
    public string HashPassword(string password, int workFactor = IPasswordHasher.InteractiveWorkFactor)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor);
    }
    public bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
        catch
        {
            return false;
        }
    }
}
