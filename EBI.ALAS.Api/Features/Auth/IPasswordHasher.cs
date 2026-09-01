namespace EBI.ALAS.Api.Features.Auth;
public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hashedPassword);
}
