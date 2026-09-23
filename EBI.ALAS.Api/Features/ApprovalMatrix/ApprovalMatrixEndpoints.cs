using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public static class ApprovalMatrixEndpoints
{
    public static void MapApprovalMatrixEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Approval Matrix")
            .RequireAuthorization();

        // GET /api/approval-authorities — the full matrix for UI rendering
        group.MapGet("/approval-authorities", async (
            AppDbContext db,
            CancellationToken ct) =>
        {
            var authorities = await db.ApprovalAuthorities
                .AsNoTracking()
                .OrderBy(a => a.Tier).ThenBy(a => a.Priority)
                .Select(a => new ApprovalAuthorityDto
                {
                    Key = a.Key,
                    DisplayName = a.DisplayName,
                    Tier = a.Tier,
                    Priority = a.Priority,
                    AllowNew = a.AllowNew,
                    AllowRenewal = a.AllowRenewal,
                    MaxSeverity = (int)a.MaxSeverity,
                    MaxTotalExposure = a.MaxTotalExposure,
                    ScopeType = (int)a.ScopeType,
                })
                .ToListAsync(ct);

            return Results.Ok(ApiResponse<List<ApprovalAuthorityDto>>.SuccessResponse(authorities));
        })
        .WithName("GetApprovalAuthorities")
        .Produces<ApiResponse<List<ApprovalAuthorityDto>>>(200)
        .RequireAuthorization("CanViewLoan");

        // GET /api/deviation-catalog — deviation severity catalog
        group.MapGet("/deviation-catalog", async (
            AppDbContext db,
            CancellationToken ct) =>
        {
            var catalog = await db.DeviationCatalog
                .AsNoTracking()
                .Select(c => new DeviationCatalogDto
                {
                    Id = c.Id,
                    Description = c.Description,
                    Severity = (int)c.Severity,
                })
                .ToListAsync(ct);

            return Results.Ok(ApiResponse<List<DeviationCatalogDto>>.SuccessResponse(catalog));
        })
        .WithName("GetDeviationCatalog")
        .Produces<ApiResponse<List<DeviationCatalogDto>>>(200)
        .RequireAuthorization("CanViewLoan");

        // GET /api/presence/approvers — online/reviewing snapshot
        group.MapGet("/presence/approvers", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            IPresenceService presence,
            ILoanAssignmentService assignment,
            CancellationToken ct) =>
        {
            var myBranch = principal.GetBranchCode();
            var myRole = principal.GetRole();

            var approvers = await db.Users.AsNoTracking()
                .Where(u => u.Role == Roles.Approver && u.IsActive && u.ApprovalAuthorityKey != null)
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.BranchId, u.ApprovalAuthorityKey })
                .ToListAsync(ct);

            var result = new List<ApproverPresenceDto>();
            foreach (var a in approvers)
            {
                var auth = await db.ApprovalAuthorities.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Key == a.ApprovalAuthorityKey, ct);
                if (auth is null) continue;

                // Scope check: Branch scope = same branch only
                if (auth.ScopeType == AuthorityScope.Branch && a.BranchId != myBranch)
                    continue;

                result.Add(new ApproverPresenceDto
                {
                    UserId = a.Id,
                    Name = $"{a.FirstName} {a.LastName}",
                    AuthorityKey = a.ApprovalAuthorityKey!,
                    Tier = auth.Tier,
                    Online = presence.IsOnline(a.Id),
                    Reviewing = await assignment.IsReviewingAsync(a.Id, ct),
                });
            }

            return Results.Ok(ApiResponse<List<ApproverPresenceDto>>.SuccessResponse(result));
        })
        .WithName("GetApproverPresence")
        .Produces<ApiResponse<List<ApproverPresenceDto>>>(200)
        .RequireAuthorization("CanViewLoan");
    }
}
