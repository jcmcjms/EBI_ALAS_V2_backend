namespace EBI.ALAS.Api.Features.Auth;
public interface IPasswordHasher
{
    const int InteractiveWorkFactor = 14;
    const int TemporaryWorkFactor = 12;
    string HashPassword(string password, int workFactor = InteractiveWorkFactor);
    bool VerifyPassword(string password, string hashedPassword);
}
