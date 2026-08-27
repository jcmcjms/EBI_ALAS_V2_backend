namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for authentication data access operations.
/// </summary>
public interface IAuthRepository
{
    Task<User?> GetUserByUsernameAsync(string username);
    Task<User?> GetUserByIdAsync(int userId);
    Task<bool> UserExistsAsync(string username);
    Task UpdateUserAsync(User user);
}
