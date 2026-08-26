namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for password hashing operations using BCrypt.
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hashedPassword);
}
