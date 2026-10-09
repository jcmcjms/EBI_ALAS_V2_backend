using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Pagination;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.AuditLogs;

public static class AuditLogEndpoints
{
    public static void MapAuditLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/audit-logs").WithTags("AuditLogs");

        group.MapGet("/", async (
            AlasDbContext db,
            int? page,
            int? pageSize,
            string? search,
            string? action,
            string? entityType,
            DateTimeOffset? startDate,
            DateTimeOffset? endDate,
            Microsoft.Extensions.Options.IOptions<Composition.ApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            var opt = options.Value;
            var pageRequest = new PageRequest { Page = page ?? 1, PageSize = pageSize ?? opt.DefaultPageSize }
                .Normalize(opt.MaxPageSize, opt.DefaultPageSize);

            var query = db.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(a =>
                    a.UserName.ToLower().Contains(term)
                    || a.EntityLabel.ToLower().Contains(term)
                    || a.Summary.ToLower().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(a => a.Action == action);
            }

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                query = query.Where(a => a.EntityType == entityType);
            }

            if (startDate is { } from)
            {
                query = query.Where(a => a.Timestamp >= from);
            }

            if (endDate is { } to)
            {
                query = query.Where(a => a.Timestamp <= to);
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip(pageRequest.Skip)
                .Take(pageRequest.PageSize)
                .Select(a => new AuditLogResponse(
                    a.Id,
                    a.Timestamp,
                    a.UserId,
                    a.UserName,
                    a.Action,
                    a.EntityType,
                    a.EntityId,
                    a.EntityLabel,
                    a.Summary,
                    a.RawChanges,
                    a.IpAddress,
                    a.UserAgent))
                .ToListAsync(cancellationToken);

            return Results.Ok(new PageResult<AuditLogResponse>(items, total, pageRequest.Page, pageRequest.PageSize));
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" });

        group.MapGet("/{id:guid}", async (
            Guid id,
            AlasDbContext db,
            CancellationToken cancellationToken) =>
        {
            var log = await db.AuditLogs.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (log is null)
            {
                throw new NotFoundException("Audit log", id.ToString());
            }

            return Results.Ok(new AuditLogResponse(
                log.Id,
                log.Timestamp,
                log.UserId,
                log.UserName,
                log.Action,
                log.EntityType,
                log.EntityId,
                log.EntityLabel,
                log.Summary,
                log.RawChanges,
                log.IpAddress,
                log.UserAgent));
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" });
    }
}
