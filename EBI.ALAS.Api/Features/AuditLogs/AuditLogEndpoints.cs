using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.AuditLogs;

// ─── Query Parameters ──────────────────────────────────────────────────────────

/// <summary>Query parameters for the audit log list endpoint.</summary>
public record AuditLogQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    string? Action = null,
    string? EntityType = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null
);

/// <summary>FluentValidation validator for AuditLogQuery.</summary>
public class AuditLogQueryValidator : AbstractValidator<AuditLogQuery>
{
    private static readonly string[] ValidActions = { "Create", "Update", "StatusChange", "Login", "Logout", "Delete" };
    private static readonly string[] ValidEntityTypes = { "LoanApplication", "User", "Auth", "Branch", "Role", "LoanProduct" };

    public AuditLogQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be between 1 and 100");

        RuleFor(x => x.Action)
            .Must(a => string.IsNullOrEmpty(a) || ValidActions.Contains(a))
            .WithMessage($"Action must be one of: {string.Join(", ", ValidActions)}");

        RuleFor(x => x.EntityType)
            .Must(e => string.IsNullOrEmpty(e) || ValidEntityTypes.Contains(e))
            .WithMessage($"EntityType must be one of: {string.Join(", ", ValidEntityTypes)}");
    }
}

// ─── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>Audit log record returned by the list endpoint.</summary>
public record AuditLogResponse(
    int Id,
    DateTime Timestamp,
    int? UserId,
    string UserName,
    string Action,
    string EntityType,
    string EntityId,
    string EntityLabel,
    string Summary,
    string? RawChanges,
    string? IpAddress,
    string? UserAgent
);

// ─── Endpoint Mapping ───────────────────────────────────────────────────────────

public static class AuditLogEndpoints
{
    public static void MapAuditLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit-logs")
            .WithTags("AuditLogs")
            .RequireAuthorization("CanViewAuditLogs");

        // GET /api/audit-logs — paginated, filterable audit log list
        group.MapGet("/", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? search,
            [FromQuery] string? action,
            [FromQuery] string? entityType,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            IValidator<AuditLogQuery> validator,
            AppDbContext db) =>
        {
            var query = new AuditLogQuery(page, pageSize, search, action, entityType, startDate, endDate);

            var validationResult = await validator.ValidateAsync(query);
            if (!validationResult.IsValid)
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Invalid query parameters",
                    validationResult.Errors.Select(e => e.ErrorMessage).ToList()));
            }

            var q = db.AuditLogs.AsNoTracking().AsQueryable();

            // Text search across user name, entity label, and summary
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var searchLower = query.Search.ToLower();
                q = q.Where(x =>
                    x.UserName.ToLower().Contains(searchLower) ||
                    x.EntityLabel.ToLower().Contains(searchLower) ||
                    x.Summary.ToLower().Contains(searchLower));
            }

            // Filter by action type
            if (!string.IsNullOrWhiteSpace(query.Action))
                q = q.Where(x => x.Action == query.Action);

            // Filter by entity type
            if (!string.IsNullOrWhiteSpace(query.EntityType))
                q = q.Where(x => x.EntityType == query.EntityType);

            // Date range filter
            if (query.StartDate.HasValue)
                q = q.Where(x => x.Timestamp >= query.StartDate.Value);

            if (query.EndDate.HasValue)
                q = q.Where(x => x.Timestamp <= query.EndDate.Value);

            var totalCount = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.Timestamp)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(x => new AuditLogResponse(
                    x.Id,
                    x.Timestamp,
                    x.UserId,
                    x.UserName,
                    x.Action,
                    x.EntityType,
                    x.EntityId,
                    x.EntityLabel,
                    x.Summary,
                    x.RawChanges,
                    x.IpAddress,
                    x.UserAgent
                ))
                .ToListAsync();

            return Results.Ok(ApiResponse<PagedResult<AuditLogResponse>>.SuccessResponse(
                PagedResult<AuditLogResponse>.Create(items, totalCount, query.Page, query.PageSize),
                "Audit logs retrieved"));
        })
        .WithName("GetAuditLogs")
        .Produces<ApiResponse<PagedResult<AuditLogResponse>>>(200)
        .Produces<ApiResponse>(400);

        // GET /api/audit-logs/{id} — single audit log record
        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var log = await db.AuditLogs
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new AuditLogResponse(
                    x.Id,
                    x.Timestamp,
                    x.UserId,
                    x.UserName,
                    x.Action,
                    x.EntityType,
                    x.EntityId,
                    x.EntityLabel,
                    x.Summary,
                    x.RawChanges,
                    x.IpAddress,
                    x.UserAgent
                ))
                .FirstOrDefaultAsync();

            return log is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Audit log entry not found"))
                : Results.Ok(ApiResponse<AuditLogResponse>.SuccessResponse(log));
        })
        .WithName("GetAuditLogById")
        .Produces<ApiResponse<AuditLogResponse>>(200)
        .Produces<ApiResponse>(404);
    }
}
