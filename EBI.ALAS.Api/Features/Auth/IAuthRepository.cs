namespace EBI.ALAS.Api.Features.Auth;
public interface IAuthRepository
{
    Task<User?> GetUserByUsernameAsync(string username);
    Task<User?> GetUserByIdAsync(int userId);
    Task<bool> UserExistsAsync(string username);
    Task UpdateUserAsync(User user);
}
