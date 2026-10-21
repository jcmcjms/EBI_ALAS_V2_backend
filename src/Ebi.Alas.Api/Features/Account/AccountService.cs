using Ebi.Alas.Api.Features.Auth.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Account;

public enum SessionRevokeResult
{
    Revoked,
    CurrentSession,
    NotFound,
}

public sealed record AccountStatsDto(
    int ProcessedLoans,
    int PendingLoans,
    int ApprovalRate);

public sealed record AccountProfileDto(
    Guid Id,
    string Username,
    string FirstName,
    string? MiddleName,
    string LastName,
    string BranchId,
    string Role,
    string? Email,
    string? Phone,
    string? EmergencyContact,
    string? ESignature,
    string? ProfilePhotoUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PasswordChangedAt,
    AccountStatsDto Stats,
    bool IsAvailable = true);

public sealed record AccountActivityDto(
    Guid Id,
    string LamId,
    string Action,
    string? FromStatus,
    string? ToStatus,
    string? Comments,
    DateTimeOffset ActionDate,
    string LoanClientName,
    bool IsInformal);

public sealed record ProcessedLoanDto(
    Guid Id,
    string LamId,
    string ClientName,
    string Status,
    DateTimeOffset ApplicationDate,
    decimal ProposedAmount);

public sealed record RecentClientDto(
    string? CisId,
    string Name,
    string Agency,
    DateTimeOffset LastInteraction);

public sealed record SessionDto(
    Guid Id,
    string? DeviceInfo,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

public sealed class AccountService(AlasDbContext db, TimeProvider timeProvider)
{
    private static readonly LoanStatus[] PendingStatuses =
    [
        LoanStatus.Draft,
        LoanStatus.ForRecommendation,
        LoanStatus.ForChecking,
        LoanStatus.ForApproval,
    ];

    private static readonly LoanStatus[] ApprovedStatuses =
    [
        LoanStatus.Approved,
        LoanStatus.Disbursed,
        LoanStatus.OnGoing,
    ];

    public async Task<AccountProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var stats = await GetStatsAsync(userId, cancellationToken);
        return new AccountProfileDto(
            user.Id,
            user.UserName,
            user.FirstName,
            string.IsNullOrWhiteSpace(user.MiddleName) ? null : user.MiddleName,
            user.LastName,
            user.BranchId,
            user.Role.ToString(),
            string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
            user.Phone,
            user.EmergencyContact,
            user.ESignature,
            ProfilePhotoUrl: null,
            user.CreatedAt,
            PasswordChangedAt: null,
            stats,
            user.IsAvailable);
    }

    public async Task<bool> UpdateContactAsync(
        Guid userId,
        string? email,
        string? phone,
        string? emergencyContact,
        string? eSignature,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.UpdateContact(email, phone, emergencyContact, timeProvider.GetUtcNow());

        // null means "do not change signature"; empty string clears it.
        if (eSignature is not null)
        {
            user.SetSignature(eSignature, timeProvider.GetUtcNow());

            if (!string.IsNullOrWhiteSpace(user.ESignature))
            {
                // Slots may have been signed before the profile ink existed.
                var signedSlots = await db.SignatureChainEntries
                    .Where(s => s.UserId == userId && s.SignatureImage == null)
                    .ToListAsync(cancellationToken);
                foreach (var slot in signedSlots)
                {
                    slot.AttachSignatureImageIfMissing(user.ESignature);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AccountActivityDto>> GetRecentActivityAsync(
        Guid userId,
        int limit,
        DateOnly? from,
        DateOnly? to,
        string? actionKind,
        CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to)
        {
            throw new Composition.Errors.ValidationException(
                "Activity date range start must be on or before end.");
        }

        DateTimeOffset? fromInclusive = from is { } f
            ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        DateTimeOffset? toExclusive = to is { } t
            ? new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

        var query = db.LoanApplicationActions.AsNoTracking()
            .Where(a => a.ActionByUserId == userId);

        if (fromInclusive is { } start)
        {
            query = query.Where(a => a.ActionDate >= start);
        }

        if (toExclusive is { } end)
        {
            query = query.Where(a => a.ActionDate < end);
        }

        var rows = await query
            .OrderByDescending(a => a.ActionDate)
            .Join(
                db.LoanApplications.AsNoTracking(),
                a => a.LoanApplicationId,
                l => l.Id,
                (a, l) => new { a, l.LamId, l.ClientName })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => MatchesActionKind(r.a.Action, actionKind ?? string.Empty))
            .Take(limit)
            .Select(r => new AccountActivityDto(
                r.a.Id,
                r.LamId,
                r.a.Action,
                r.a.FromStatus,
                r.a.ToStatus,
                r.a.Comments,
                r.a.ActionDate,
                r.ClientName,
                IsInformalComment(r.a.Comments)))
            .ToList();
    }

    private static bool IsInformalComment(string? comments)
    {
        if (string.IsNullOrWhiteSpace(comments))
        {
            return false;
        }

        var trimmed = comments.Trim();
        return trimmed.StartsWith("test", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("testing", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("sample", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesActionKind(string action, string actionKind)
    {
        var a = action ?? string.Empty;
        return actionKind switch
        {
            "login" => (a.Contains("login", StringComparison.OrdinalIgnoreCase)
                        || a.Contains("sign", StringComparison.OrdinalIgnoreCase))
                       && !a.Contains("password", StringComparison.OrdinalIgnoreCase),
            "security" => a.Contains("password", StringComparison.OrdinalIgnoreCase),
            "draft" => a.Contains("draft", StringComparison.OrdinalIgnoreCase)
                       || a.Contains("saved", StringComparison.OrdinalIgnoreCase),
            "approval" => a.Contains("recommend", StringComparison.OrdinalIgnoreCase)
                          || a.Contains("approv", StringComparison.OrdinalIgnoreCase)
                          || a.Contains("release", StringComparison.OrdinalIgnoreCase),
            "application" => a.Contains("application", StringComparison.OrdinalIgnoreCase)
                             || a.Contains("submit", StringComparison.OrdinalIgnoreCase)
                             || a.Contains("create", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    public async Task<IReadOnlyList<ProcessedLoanDto>> GetProcessedLoansAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await db.LoanApplications.AsNoTracking()
            .Where(l => l.CreatedBy == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new ProcessedLoanDto(
                l.Id,
                l.LamId,
                l.ClientName,
                l.Status.ToString(),
                l.CreatedAt,
                l.Principal))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecentClientDto>> GetRecentClientsAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await db.LoanApplications.AsNoTracking()
            .Where(l => l.CreatedBy == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new RecentClientDto(
                null,
                l.ClientName,
                string.Empty,
                l.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.IpAddress,
                t.UserAgent,
                t.CreatedAt,
                t.ExpiresAt,
                IsCurrent = currentSessionId != null && t.Id == currentSessionId
            })
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(t => new SessionDto(
            t.Id,
            Auth.SessionClientInfo.Parse(t.IpAddress, t.UserAgent).DeviceInfo,
            t.IpAddress,
            t.CreatedAt,
            t.ExpiresAt,
            t.IsCurrent));
    }

    public async Task<SessionRevokeResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        if (currentSessionId is { } current && current == sessionId)
        {
            return SessionRevokeResult.CurrentSession;
        }

        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(
                t => t.Id == sessionId && t.UserId == userId && t.RevokedAt == null,
                cancellationToken);
        if (token is null)
        {
            return SessionRevokeResult.NotFound;
        }

        token.Revoke(timeProvider.GetUtcNow(), Guid.Empty);
        await db.SaveChangesAsync(cancellationToken);
        return SessionRevokeResult.Revoked;
    }

    public async Task<int> RevokeOtherSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId
                && t.RevokedAt == null
                && t.ExpiresAt > now
                && (currentSessionId == null || t.Id != currentSessionId))
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0)
        {
            return 0;
        }

        foreach (var token in tokens)
        {
            token.Revoke(now, Guid.Empty);
        }

        await db.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }

    public async Task<Guid?> FindSessionIdByRawTokenAsync(
        Guid userId,
        string rawToken,
        CancellationToken cancellationToken)
    {
        var hash = Auth.TokenStore.HashToken(rawToken);
        return await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.TokenHash == hash)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<AccountStatsDto> GetStatsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var totalLoans = await db.LoanApplications
            .CountAsync(l => l.CreatedBy == userId, cancellationToken);
        var pendingLoans = await db.LoanApplications
            .CountAsync(l => l.CreatedBy == userId && PendingStatuses.Contains(l.Status), cancellationToken);
        var approvedLoans = await db.LoanApplications
            .CountAsync(l => l.CreatedBy == userId && ApprovedStatuses.Contains(l.Status), cancellationToken);
        var approvalRate = totalLoans > 0 ? (int)((double)approvedLoans / totalLoans * 100) : 0;
        return new AccountStatsDto(totalLoans, pendingLoans, approvalRate);
    }
}
