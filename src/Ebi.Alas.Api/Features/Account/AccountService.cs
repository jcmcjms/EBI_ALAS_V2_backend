using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Account;

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
    string? ProfilePhotoUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PasswordChangedAt,
    AccountStatsDto Stats);

public sealed record AccountActivityDto(
    Guid Id,
    string LamId,
    string Action,
    string? FromStatus,
    string? ToStatus,
    string? Comments,
    DateTimeOffset ActionDate,
    string LoanClientName);

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

public sealed class AccountService(AlasDbContext db)
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
            Phone: null,
            EmergencyContact: null,
            ProfilePhotoUrl: null,
            user.CreatedAt,
            PasswordChangedAt: null,
            stats);
    }

    public async Task<IReadOnlyList<AccountActivityDto>> GetRecentActivityAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.LoanApplicationActions.AsNoTracking()
            .Where(a => a.ActionByUserId == userId)
            .OrderByDescending(a => a.ActionDate)
            .Take(limit)
            .Join(
                db.LoanApplications.AsNoTracking(),
                a => a.LoanApplicationId,
                l => l.Id,
                (a, l) => new { a, l.LamId, l.ClientName })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AccountActivityDto(
            r.a.Id,
            r.LamId,
            r.a.Action,
            r.a.FromStatus,
            r.a.ToStatus,
            r.a.Comments,
            r.a.ActionDate,
            r.ClientName)).ToList();
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
