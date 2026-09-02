using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Account;

public class AccountRepository : IAccountRepository
{
    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;

    public AccountRepository(AppDbContext context, ITimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<AccountProfileResponse?> GetProfileAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return null;

        var stats = await GetStatsAsync(userId);

        return new AccountProfileResponse(
            user.Id,
            user.Username,
            user.FirstName,
            user.MiddleName,
            user.LastName,
            user.BranchId,
            user.Role,
            user.Email,
            user.Phone,
            user.EmergencyContact,
            user.ProfilePhotoUrl,
            user.CreatedAt,
            user.PasswordChangedAt,
            stats
        );
    }

    public async Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        user.Email = request.Email;
        user.Phone = request.Phone;
        user.EmergencyContact = request.EmergencyContact;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<PagedSessionsResponse> GetActiveSessionsAsync(int userId, int currentSessionId, int pageNumber = 1, int pageSize = 10)
    {
        var query = _context.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked && t.ExpiresAt > _timeProvider.UtcNow)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        
        // Project only raw columns in SQL — the DeviceInfo → "Browser on OS"
        // label is computed in memory below via UserAgentParser.Describe.
        // Calling the static parser inside the EF projection would throw
        // "could not be translated" at runtime (Regex.IsMatch has no SQL
        // equivalent), which surfaces to the UI as a generic 500 and was
        // the root cause of the "Unable to load client profile" toast
        // seen during the CIS lookup page load.
        var rows = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.DeviceInfo,
                t.CreatedAt,
                t.ExpiresAt,
                IsCurrent = t.Id == currentSessionId
            })
            .ToListAsync();

        var items = rows
            .Select(r => new SessionResponse(
                r.Id,
                // Translate the raw User-Agent into a short "Browser on OS"
                // label for the UI. We keep the raw UA in the DB for forensics,
                // but display the parsed form. Falls back to "Unknown Device"
                // for null UAs (legacy rows issued before capture was wired in).
                UserAgentParser.Describe(r.DeviceInfo),
                r.CreatedAt,
                r.ExpiresAt,
                r.IsCurrent
            ))
            .ToList();

        return new PagedSessionsResponse(
            items,
            pageNumber,
            pageSize,
            totalCount,
            totalPages,
            pageNumber > 1,
            pageNumber < totalPages
        );
    }

    public async Task<bool> RevokeSessionAsync(int userId, int sessionId)
    {
        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.Id == sessionId && t.UserId == userId && !t.IsRevoked);

        if (token == null) return false;

        token.IsRevoked = true;
        token.RevokedAt = _timeProvider.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<ActivityResponse>> GetRecentActivityAsync(int userId, int limit = 10)
    {
        return await _context.LoanActions
            .Where(a => a.ActionByUserId == userId)
            .OrderByDescending(a => a.ActionDate)
            .Take(limit)
            .Select(a => new ActivityResponse(
                a.Id,
                a.LoanApplication.FormNumber,
                a.Action,
                a.FromStatus,
                a.ToStatus,
                a.Comments,
                a.ActionDate,
                $"{a.LoanApplication.FirstName} {a.LoanApplication.LastName}"
            ))
            .ToListAsync();
    }

    public async Task<List<ProcessedLoanResponse>> GetProcessedLoansAsync(int userId, int limit = 10)
    {
        return await _context.LoanApplications
            .Where(l => l.CreatedById == userId)
            .OrderByDescending(l => l.ApplicationDate)
            .Take(limit)
            .Select(l => new ProcessedLoanResponse(
                l.Id,
                l.FormNumber,
                $"{l.FirstName} {l.LastName}",
                l.Status,
                l.ApplicationDate,
                l.ProposedAmount
            ))
            .ToListAsync();
    }

    public async Task<List<RecentClientResponse>> GetRecentClientsAsync(int userId, int limit = 5)
    {
        return await _context.LoanApplications
            .Where(l => l.CreatedById == userId && l.CisId != null)
            .OrderByDescending(l => l.ApplicationDate)
            .Take(limit)
            .Select(l => new RecentClientResponse(
                l.CisId!,
                $"{l.FirstName} {l.LastName}",
                l.Agency ?? "",
                l.ApplicationDate
            ))
            .ToListAsync();
    }

    public async Task<AccountStatsResponse> GetStatsAsync(int userId)
    {
        var totalLoans = await _context.LoanApplications
            .CountAsync(l => l.CreatedById == userId);

        var pendingLoans = await _context.LoanApplications
            .CountAsync(l => l.CreatedById == userId && 
                           (l.Status == "Draft" || l.Status == "ForRecommendation" || 
                            l.Status == "ForChecking" || l.Status == "ForApproval"));

        var approvedLoans = await _context.LoanApplications
            .CountAsync(l => l.CreatedById == userId && 
                           (l.Status == "Approved" || l.Status == "Disbursed" || l.Status == "OnGoing"));

        var approvalRate = totalLoans > 0 ? (int)((double)approvedLoans / totalLoans * 100) : 0;

        return new AccountStatsResponse(totalLoans, pendingLoans, approvalRate);
    }
}