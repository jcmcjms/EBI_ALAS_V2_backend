using Alas.Application.Audit;
using Alas.Infrastructure.Auditing;
using Alas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Alas.Infrastructure.Services;

public sealed class AuditQueryService
{
    private readonly AlasDbContext _context;

    public AuditQueryService(AlasDbContext context)
    {
        _context = context;
    }

    public async Task<AuditPagedResult> QueryAsync(
        AuditQueryParams parameters,
        CancellationToken cancellationToken)
    {
        var query = _context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(parameters.Action))
        {
            var action = parameters.Action.Trim();
            query = query.Where(a => a.Action.Contains(action));
        }

        if (!string.IsNullOrWhiteSpace(parameters.Username))
        {
            var username = parameters.Username.Trim();
            query = query.Where(a => a.Username != null && a.Username.Contains(username));
        }

        if (parameters.UserId.HasValue)
        {
            query = query.Where(a => a.UserId == parameters.UserId.Value);
        }

        if (parameters.FromUtc.HasValue)
        {
            query = query.Where(a => a.UtcTimestamp >= parameters.FromUtc.Value);
        }

        if (parameters.ToUtc.HasValue)
        {
            query = query.Where(a => a.UtcTimestamp <= parameters.ToUtc.Value);
        }

        if (parameters.IsSuccess.HasValue)
        {
            query = query.Where(a => a.IsSuccess == parameters.IsSuccess.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.UtcTimestamp)
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(a => new AuditLogDto(
                a.Id,
                a.UtcTimestamp,
                a.Action,
                a.IsSuccess,
                a.UserId,
                a.Username,
                a.EntityType,
                a.EntityId,
                a.Details,
                a.FailureReason,
                a.IpAddress,
                a.CorrelationId))
            .ToListAsync(cancellationToken);

        return new AuditPagedResult(
            items, totalCount, parameters.Page, parameters.PageSize);
    }

    public async Task<AuditPagedResult> GetLoginHistoryAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await QueryAsync(new AuditQueryParams
        {
            Page = page,
            PageSize = pageSize,
            Action = "Auth.Login"
        }, cancellationToken);
    }

    public async Task<AuditPagedResult> GetPermissionChangesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await QueryAsync(new AuditQueryParams
        {
            Page = page,
            PageSize = pageSize,
            Action = "Permission"
        }, cancellationToken);
    }

    public async Task<AuditPagedResult> GetRoleChangesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await QueryAsync(new AuditQueryParams
        {
            Page = page,
            PageSize = pageSize,
            Action = "Role"
        }, cancellationToken);
    }

    public async Task<AuditPagedResult> GetLoanEventsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await QueryAsync(new AuditQueryParams
        {
            Page = page,
            PageSize = pageSize,
            Action = "Loan"
        }, cancellationToken);
    }
}
