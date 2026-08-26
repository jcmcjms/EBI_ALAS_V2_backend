using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Users;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public UserService(IUserRepository userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<PagedResult<UserResponse>> GetUsersAsync(UserQueryParameters parameters) =>
        await _userRepository.GetUsersAsync(parameters);

    public async Task<UserResponse?> GetUserByIdAsync(int id)
    {
        var user = await _userRepository.GetUserByIdAsync(id);
        if (user == null) return null;
        return new UserResponse(user.Id, user.Username, user.FirstName, user.MiddleName, user.LastName, user.BranchId, user.Role, user.IsActive, user.CreatedAt);
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request)
    {
        if (await _userRepository.UsernameExistsAsync(request.Username))
            throw new InvalidOperationException("Username already exists");

        var user = new User
        {
            Username = request.Username,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            BranchId = request.BranchId,
            Role = request.Role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddUserAsync(user);
        return new UserResponse(user.Id, user.Username, user.FirstName, user.MiddleName, user.LastName, user.BranchId, user.Role, user.IsActive, user.CreatedAt);
    }

    public async Task<UserResponse?> UpdateUserAsync(int id, UpdateUserRequest request)
    {
        var user = await _userRepository.GetUserByIdAsync(id);
        if (user == null) return null;

        user.FirstName = request.FirstName;
        user.MiddleName = request.MiddleName;
        user.LastName = request.LastName;
        user.BranchId = request.BranchId;
        user.Role = request.Role;

        await _userRepository.UpdateUserAsync();
        return new UserResponse(user.Id, user.Username, user.FirstName, user.MiddleName, user.LastName, user.BranchId, user.Role, user.IsActive, user.CreatedAt);
    }

    public async Task<bool> UpdateUserStatusAsync(int id, bool isActive)
    {
        var user = await _userRepository.GetUserByIdAsync(id);
        if (user == null) return false;

        // Banking Rule: NEVER hard delete users. Soft delete only to preserve audit trails.
        user.IsActive = isActive;
        await _userRepository.UpdateUserAsync();
        return true;
    }
}
