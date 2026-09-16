using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Users;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITimeProvider _timeProvider;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly AppDbContext _context;

    public UserService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITimeProvider timeProvider,
        IRefreshTokenRepository refreshTokenRepository,
        AppDbContext context)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
        _refreshTokenRepository = refreshTokenRepository;
        _context = context;
    }

    public async Task<PagedResult<UserResponse>> GetUsersAsync(UserQueryParameters parameters) =>
        await _userRepository.GetUsersAsync(parameters);

    public async Task<UserResponse?> GetUserByIdAsync(int id)
    {
        var user = await _userRepository.GetUserByIdAsync(id);
        if (user == null) return null;
        return await MapToResponseAsync(user);
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
            MustChangePassword = true,
            CreatedAt = _timeProvider.UtcNow,
        };

        // ── Approver: JobTitle is the authority key, sync both fields ────
        if (request.Role == Roles.Approver && !string.IsNullOrWhiteSpace(request.JobTitle))
        {
            var authority = await _context.ApprovalAuthorities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Key == request.JobTitle);

            if (authority is null)
                throw new InvalidOperationException($"Invalid approval authority: {request.JobTitle}");

            user.ApprovalAuthorityKey = authority.Key;
            user.JobTitle = authority.DisplayName; // Store human-readable label
        }
        else
        {
            // Non-approver: free text, no authority link
            user.ApprovalAuthorityKey = null;
            user.JobTitle = request.JobTitle;
        }

        if (!string.IsNullOrWhiteSpace(request.ESignature))
            user.ESignature = request.ESignature;

        await _userRepository.AddUserAsync(user);

        // ── Multi-branch coverage for Branch-scope approvers ──────────
        if (request.Role == Roles.Approver
            && request.CoveredBranches is { Count: > 0 }
            && !string.IsNullOrWhiteSpace(request.JobTitle))
        {
            var authority = await _context.ApprovalAuthorities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Key == request.JobTitle);

            if (authority?.ScopeType == AuthorityScope.Branch)
            {
                foreach (var branchCode in request.CoveredBranches.Distinct())
                {
                    _context.UserBranchCoverages.Add(new UserBranchCoverage
                    {
                        UserId = user.Id,
                        BranchCode = branchCode
                    });
                }
                await _context.SaveChangesAsync();
            }
        }

        return await MapToResponseAsync(user);
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

        // ── Approver: sync authority key from JobTitle dropdown value ────
        if (request.Role == Roles.Approver && !string.IsNullOrWhiteSpace(request.JobTitle))
        {
            var authority = await _context.ApprovalAuthorities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Key == request.JobTitle);

            if (authority is null)
                throw new InvalidOperationException($"Invalid approval authority: {request.JobTitle}");

            user.ApprovalAuthorityKey = authority.Key;
            user.JobTitle = authority.DisplayName; // Store human-readable label
        }
        else
        {
            user.ApprovalAuthorityKey = null;
            user.JobTitle = request.JobTitle;
        }

        // Only update the signature when the client explicitly provided
        // a value. This preserves the existing base64 PNG when the user
        // edits their name or branch without touching the signature pad.
        // - ESignature == null  → no change, leave as-is
        // - ESignature == ""    → clear the signature
        // - ESignature == "..." → replace with the new payload
        if (request.ESignature is not null)
        {
            user.ESignature = string.IsNullOrEmpty(request.ESignature) ? null : request.ESignature;
        }

        await _userRepository.UpdateUserAsync();

        // ── Multi-branch coverage for Branch-scope approvers ──────────
        if (request.Role == Roles.Approver && request.CoveredBranches is not null)
        {
            var authority = await _context.ApprovalAuthorities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Key == request.JobTitle);

            if (authority?.ScopeType == AuthorityScope.Branch && request.CoveredBranches.Count > 0)
            {
                // Replace coverage: remove old, insert new
                var existing = await _context.UserBranchCoverages
                    .Where(ubc => ubc.UserId == id).ToListAsync();
                _context.UserBranchCoverages.RemoveRange(existing);

                foreach (var branchCode in request.CoveredBranches.Distinct())
                {
                    _context.UserBranchCoverages.Add(new UserBranchCoverage
                    {
                        UserId = id,
                        BranchCode = branchCode
                    });
                }
                await _context.SaveChangesAsync();
            }
            else
            {
                // Area/Global scope or empty coverage: clear any old coverage rows
                var existing = await _context.UserBranchCoverages
                    .Where(ubc => ubc.UserId == id).ToListAsync();
                if (existing.Count > 0)
                {
                    _context.UserBranchCoverages.RemoveRange(existing);
                    await _context.SaveChangesAsync();
                }
            }
        }

        return await MapToResponseAsync(user);
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

    public async Task<bool> ForcePasswordResetAsync(int id)
    {
        var user = await _userRepository.GetUserByIdAsync(id);
        if (user == null) return false;

        user.MustChangePassword = true;
        await _userRepository.UpdateUserAsync();
        return true;
    }

    public async Task<string> ResetPasswordAsync(int id, string newPassword)
    {
        var user = await _userRepository.GetUserByIdAsync(id);
        if (user == null)
            throw new NotFoundException("User", id);

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.MustChangePassword = true; // Force change on next login
        await _userRepository.UpdateUserAsync();

        return newPassword;
    }

    public async Task<int> RevokeAllSessionsAsync(int id)
    {
        var user = await _userRepository.GetUserByIdAsync(id);
        if (user == null)
            throw new NotFoundException("User", id);

        // Get count of active sessions before revoking
        var activeSessions = await _context.RefreshTokens
            .Where(rt => rt.UserId == id && rt.ExpiresAt > _timeProvider.UtcNow && !rt.IsRevoked)
            .CountAsync();

        await _refreshTokenRepository.RevokeAllUserTokensAsync(id);

        return activeSessions;
    }

    public async Task<List<UserAuditLogResponse>> GetAuditLogAsync(int userId, int pageNumber = 1, int pageSize = 20)
    {
        var query = _context.AuditLogs
            .Where(log => log.UserId == userId)
            .OrderByDescending(log => log.Timestamp);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new UserAuditLogResponse(
                log.Id,
                log.Action,
                log.EntityType,
                log.EntityLabel,
                log.Summary,
                log.Timestamp,
                log.IpAddress
            ))
            .ToListAsync();

        return items;
    }

    /// <summary>
    /// Maps a User entity to UserResponse, resolving the approval authority
    /// info and multi-branch coverage when applicable.
    /// </summary>
    private async Task<UserResponse> MapToResponseAsync(User user)
    {
        ApprovalAuthorityInfo? authorityInfo = null;
        List<string>? coveredBranches = null;

        if (!string.IsNullOrEmpty(user.ApprovalAuthorityKey))
        {
            var authority = await _context.ApprovalAuthorities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Key == user.ApprovalAuthorityKey);

            if (authority is not null)
            {
                authorityInfo = new ApprovalAuthorityInfo(
                    authority.Key,
                    authority.DisplayName,
                    authority.Tier,
                    authority.Priority,
                    authority.MaxTotalExposure);

                // Load multi-branch coverage for Branch-scope approvers
                if (authority.ScopeType == AuthorityScope.Branch)
                {
                    coveredBranches = await _context.UserBranchCoverages
                        .AsNoTracking()
                        .Where(ubc => ubc.UserId == user.Id)
                        .Select(ubc => ubc.BranchCode)
                        .ToListAsync();
                }
            }
        }

        return new UserResponse(
            user.Id,
            user.Username,
            user.FirstName,
            user.MiddleName,
            user.LastName,
            user.BranchId,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.JobTitle,
            user.ESignature,
            authorityInfo,
            coveredBranches);
    }
}
