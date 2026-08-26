using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Users;

public interface IUserService
{
    Task<PagedResult<UserResponse>> GetUsersAsync(UserQueryParameters parameters);
    Task<UserResponse?> GetUserByIdAsync(int id);
    Task<UserResponse> CreateUserAsync(CreateUserRequest request);
    Task<UserResponse?> UpdateUserAsync(int id, UpdateUserRequest request);
    Task<bool> UpdateUserStatusAsync(int id, bool isActive);
}
