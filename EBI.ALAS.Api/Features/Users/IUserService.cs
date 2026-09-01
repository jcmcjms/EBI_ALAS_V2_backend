using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Users;

public interface IUserService
{
    Task<PagedResult<UserResponse>> GetUsersAsync(UserQueryParameters parameters);
    Task<UserResponse?> GetUserByIdAsync(int id);
    Task<UserResponse> CreateUserAsync(CreateUserRequest request);
    Task<UserResponse?> UpdateUserAsync(int id, UpdateUserRequest request);
    Task<bool> UpdateUserStatusAsync(int id, bool isActive);
    Task<bool> ForcePasswordResetAsync(int id);
    Task<string> ResetPasswordAsync(int id, string newPassword);
    Task<int> RevokeAllSessionsAsync(int id);
    Task<List<UserAuditLogResponse>> GetAuditLogAsync(int userId, int pageNumber = 1, int pageSize = 20);
}
