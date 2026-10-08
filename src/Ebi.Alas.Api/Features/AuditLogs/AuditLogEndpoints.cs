using Ebi.Alas.Api.Features.AuditLogs;
using Ebi.Alas.Api.Features.Pagination;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.AuditLogs;

public static class AuditLogEndpoints
{
    public static void MapAuditLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit-logs", async (
            AlasDbContext db,
            int? page,
            int? pageSize,
            Microsoft.Extensions.Options.IOptions<Composition.ApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            var opt = options.Value;
            var pageRequest = new PageRequest { Page = page ?? 1, PageSize = pageSize ?? opt.DefaultPageSize }
                .Normalize(opt.MaxPageSize, opt.DefaultPageSize);
            var query = db.AuditLogs.AsNoTracking().OrderByDescending(a => a.CreatedAt);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(pageRequest.Skip)
                .Take(pageRequest.PageSize)
                .Select(a => new AuditLogResponse(
                    a.Id,
                    a.UserId,
                    a.Action,
                    a.EntityType,
                    a.EntityId,
                    a.CreatedAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(new PageResult<AuditLogResponse>(items, total, pageRequest.Page, pageRequest.PageSize));
        })
        .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { Roles = "Admin" })
        .WithTags("AuditLogs");
    }
}
