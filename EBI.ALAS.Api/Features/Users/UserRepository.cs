using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Users;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    public async Task<PagedResult<UserResponse>> GetUsersAsync(UserQueryParameters parameters)
    {
        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.Search))
        {
            var search = parameters.Search.ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(search) ||
                u.FirstName.ToLower().Contains(search) ||
                u.LastName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(parameters.Role))
            query = query.Where(u => u.Role == parameters.Role);

        if (parameters.IsActive.HasValue)
            query = query.Where(u => u.IsActive == parameters.IsActive.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Skip((parameters.PageNumber - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(u => new UserResponse(
                u.Id, u.Username, u.FirstName, u.MiddleName, u.LastName,
                u.BranchId, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync();

        return new PagedResult<UserResponse>(items, totalCount, parameters.PageNumber, parameters.PageSize);
    }

    public async Task<User?> GetUserByIdAsync(int id) => await _context.Users.FindAsync(id);

    public async Task<bool> UsernameExistsAsync(string username, int? excludeId = null)
    {
        var query = _context.Users.Where(u => u.Username == username);
        if (excludeId.HasValue) query = query.Where(u => u.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    public async Task AddUserAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateUserAsync() => await _context.SaveChangesAsync();
}
