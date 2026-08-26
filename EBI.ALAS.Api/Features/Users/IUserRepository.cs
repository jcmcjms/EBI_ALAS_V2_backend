using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Users;

public interface IUserRepository
{
    Task<PagedResult<UserResponse>> GetUsersAsync(UserQueryParameters parameters);
    Task<User?> GetUserByIdAsync(int id);
    Task<bool> UsernameExistsAsync(string username, int? excludeId = null);
    Task AddUserAsync(User user);
    Task UpdateUserAsync();
}
