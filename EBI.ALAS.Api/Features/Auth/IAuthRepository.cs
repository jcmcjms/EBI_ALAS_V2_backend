namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for authentication data access operations.
/// </summary>
public interface IAuthRepository
{
    Task<User?> GetUserByUsernameAsync(string username);
    Task<bool> UserExistsAsync(string username);
}
