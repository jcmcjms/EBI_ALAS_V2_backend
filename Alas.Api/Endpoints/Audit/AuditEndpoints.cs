using Alas.Application.Audit;
using Alas.Application.Common.Security;
using Alas.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alas.Api.Endpoints.Audit;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit")
            .RequireAuthorization(AlasPermissions.AuditRead)
            .WithTags("Audit");

        group.MapGet("/", QueryAuditLogsAsync)
            .Produces<AuditPagedResult>();

        group.MapGet("/login-history", GetLoginHistoryAsync)
            .Produces<AuditPagedResult>();

        group.MapGet("/permission-changes", GetPermissionChangesAsync)
            .Produces<AuditPagedResult>();

        group.MapGet("/role-changes", GetRoleChangesAsync)
            .Produces<AuditPagedResult>();

        group.MapGet("/loan-events", GetLoanEventsAsync)
            .Produces<AuditPagedResult>();

        return app;
    }

    private static async Task<IResult> QueryAuditLogsAsync(
        [AsParameters] AuditQueryParams parameters,
        AuditQueryService auditService,
        CancellationToken cancellationToken)
    {
        var result = await auditService.QueryAsync(parameters, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetLoginHistoryAsync(
        [AsParameters] AuditPageParams pagination,
        AuditQueryService auditService,
        CancellationToken cancellationToken)
    {
        var result = await auditService.GetLoginHistoryAsync(
            pagination.Page, pagination.PageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPermissionChangesAsync(
        [AsParameters] AuditPageParams pagination,
        AuditQueryService auditService,
        CancellationToken cancellationToken)
    {
        var result = await auditService.GetPermissionChangesAsync(
            pagination.Page, pagination.PageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetRoleChangesAsync(
        [AsParameters] AuditPageParams pagination,
        AuditQueryService auditService,
        CancellationToken cancellationToken)
    {
        var result = await auditService.GetRoleChangesAsync(
            pagination.Page, pagination.PageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetLoanEventsAsync(
        [AsParameters] AuditPageParams pagination,
        AuditQueryService auditService,
        CancellationToken cancellationToken)
    {
        var result = await auditService.GetLoanEventsAsync(
            pagination.Page, pagination.PageSize, cancellationToken);
        return Results.Ok(result);
    }
}

public sealed record AuditPageParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
