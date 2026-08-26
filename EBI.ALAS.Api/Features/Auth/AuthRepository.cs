using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Authentication repository implementation using Entity Framework Core.
/// </summary>
public class AuthRepository : IAuthRepository
{
    private readonly AppDbContext _context;

    public AuthRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
    }

    public async Task<bool> UserExistsAsync(string username)
    {
        return await _context.Users.AnyAsync(u => u.Username == username);
    }
}
