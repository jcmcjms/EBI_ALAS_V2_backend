using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Loans.DTOs;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class GetLoans
{
    public static void MapGetLoansEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapGet("/", async (
            HttpContext ctx,
            AppDbContext db,
            int? page,
            int? pageSize,
            string? search,
            string? status,
            string? branchCode,
            string? sortBy,
            bool? sortDesc,
            DateTime? fromDate,
            DateTime? toDate,
            bool? myTurn,
            CancellationToken ct) =>
        {
            var p = Math.Max(page ?? 1, 1);
            var ps = Math.Clamp(pageSize ?? 15, 1, 100);

            IQueryable<LoanApplication> query = db.LoanApplications
                .AsNoTracking()
                .Include(l => l.CreatedBy)
                .Include(l => l.AssignedApprover)
                .Include(l => l.DocumentsFlaggedBy)
                .Include(l => l.DocumentChecklists);

            var userRole = ctx.User.GetRole();
            var userBranchCode = ctx.User.GetBranchCode();
            var userId = ctx.User.GetUserId();

            if (!string.IsNullOrEmpty(userBranchCode)
                && !string.Equals(userRole, Roles.Admin, StringComparison.Ordinal))
            {
                query = query.Where(l => l.BranchCode == userBranchCode);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(l =>
                    l.ApplicationGroupNo.Contains(s) ||
                    l.FirstName.Contains(s) ||
                    l.LastName.Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var statuses = status
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
                if (statuses.Length > 0)
                {
                    query = query.Where(l => statuses.Contains(l.Status));
                }
            }

            if (string.Equals(userRole, Roles.Admin, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(branchCode)
                && !string.Equals(branchCode, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(l => l.BranchCode == branchCode);
            }

            if (fromDate.HasValue)
            {
                var startDate = fromDate.Value.Date;
                query = query.Where(l => l.ApplicationDate >= startDate);
            }
            if (toDate.HasValue)
            {
                var endDate = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(l => l.ApplicationDate <= endDate);
            }

            // "My turn" filter: only loans where the current user is the head owner
            if (myTurn == true)
            {
                query = query.Where(l => db.WorkflowQueueItems.Any(i =>
                    i.LoanApplicationId == l.Id && i.State == QueueItemState.Active
                    && i.OwnerUserId == userId));
            }

            query = sortBy?.ToLower() switch
            {
                "applicationdate" => sortDesc == true
                    ? query.OrderByDescending(l => l.ApplicationDate)
                    : query.OrderBy(l => l.ApplicationDate),
                "proposedamount" => sortDesc == true
                    ? query.OrderByDescending(l => l.ProposedAmount)
                    : query.OrderBy(l => l.ProposedAmount),
                "status" => sortDesc == true
                    ? query.OrderByDescending(l => l.Status)
                    : query.OrderBy(l => l.Status),
                "customername" => sortDesc == true
                    ? query.OrderByDescending(l => l.LastName)
                    : query.OrderBy(l => l.LastName),
                _ => query.OrderByDescending(l => l.ApplicationDate)
            };

            var totalCount = await query.CountAsync(ct);

            var rows = await query
                .Select(l => new
                {
                    l.ApplicationGroupNo,
                    l.Id,
                    l.LamId,
                    l.LoanNo,
                    l.ProductCode,
                    l.Product,
                    l.ProposedAmount,
                    l.Status,
                    l.BranchCode,
                    l.CreationTypeCode,
                    l.CreationTypeLabel,
                    l.FirstName,
                    l.MiddleName,
                    l.LastName,
                    l.Suffix,
                    l.ApplicationDate,
                    l.LastActionDate,
                    l.CreatedById,
                    CreatedByName = l.CreatedBy.FirstName + " " + l.CreatedBy.LastName,
                    l.DocumentsCompleteAt,
                    l.RequiredApprovalTier,
                    l.AssignedApproverId,
                    AssignedApproverName = l.AssignedApprover == null
                        ? null
                        : l.AssignedApprover.FirstName + " " + l.AssignedApprover.LastName,
                    l.DocumentsFlaggedAt,
                    l.DocumentFlagReason,
                    DocumentsFlaggedByName = l.DocumentsFlaggedBy == null
                        ? null
                        : l.DocumentsFlaggedBy.FirstName + " " + l.DocumentsFlaggedBy.LastName,
                    DocumentMissingCount = l.DocumentChecklists.Count(d => d.Status == "Missing" || d.Status == "Pending"),
                    LastActionInfo = l.Actions
                        .OrderByDescending(a => a.ActionDate)
                        .ThenByDescending(a => a.Id)
                        .Select(a => new
                        {
                            Name = a.ActionByUser.FirstName + " " + a.ActionByUser.LastName,
                            a.Action,
                        })
                        .FirstOrDefault(),
                })
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToListAsync(ct);

            // Resolve queue positions for this page
            var queueService = ctx.RequestServices.GetRequiredService<IWorkflowQueueService>();
            var loanIds = rows.Select(r => r.Id).ToList();
            var positions = await queueService.GetPositionsAsync(loanIds, ct);

            var submissions = rows
                .GroupBy(r => r.ApplicationGroupNo)
                .Select(g => new LoanSubmissionResponse
                {
                    ApplicationGroupNo = g.Key,
                    Loans = g
                        .Select(r =>
                        {
                            positions.TryGetValue(r.Id, out var queueInfo);
                            return new CreatedLoan
                            {
                                Id = r.Id,
                                LamId = r.LamId,
                                LoanNo = r.LoanNo,
                                ProductCode = r.ProductCode,
                                Product = r.Product,
                                ProposedAmount = r.ProposedAmount,
                                Status = r.Status,
                                BranchCode = r.BranchCode,
                                CreationTypeCode = r.CreationTypeCode,
                                CreationTypeLabel = r.CreationTypeLabel,
                                FirstName = r.FirstName,
                                MiddleName = r.MiddleName,
                                LastName = r.LastName,
                                Suffix = r.Suffix,
                                ApplicationDate = r.ApplicationDate,
                                LastActionDate = r.LastActionDate,
                                CreatedById = r.CreatedById,
                                CreatedByName = r.CreatedByName,
                                LastActionByName = r.LastActionInfo != null ? r.LastActionInfo.Name : r.CreatedByName,
                                LastAction = r.LastActionInfo != null ? r.LastActionInfo.Action : null,
                                DocumentsComplete = r.DocumentsCompleteAt != null,
                                DocumentsCompleteAt = r.DocumentsCompleteAt,
                                RequiredApprovalTier = r.RequiredApprovalTier,
                                AssignedApproverId = r.AssignedApproverId,
                                AssignedApproverName = r.AssignedApproverName,
                                // Queue position fields
                                QueueStage = queueInfo?.Stage.ToString(),
                                QueuePosition = queueInfo?.Position,
                                QueueLength = queueInfo?.QueueLength,
                                QueueOwnerName = queueInfo?.OwnerName,
                                IsQueueHead = queueInfo?.IsHead ?? false,
                                DocumentFlag = r.DocumentsFlaggedAt != null
                                    ? new DocumentFlagDto(
                                        r.DocumentsFlaggedAt.Value,
                                        null, // flaggedById not needed for list view
                                        r.DocumentFlagReason,
                                        r.DocumentMissingCount)
                                    : null,
                            };
                        })
                        .ToList(),
                })
                .ToList();

            var pagedResult = new PagedResult<LoanSubmissionResponse>(
                submissions, totalCount, p, ps);

            return Results.Ok(ApiResponse<PagedResult<LoanSubmissionResponse>>.SuccessResponse(
                pagedResult, "Loans retrieved successfully"));
        })
        .WithName("GetLoans")
        .Produces<ApiResponse<PagedResult<LoanSubmissionResponse>>>(200)
        .Produces<ApiResponse>(401)
        .Produces<ApiResponse>(403)
        .RequireAuthorization("CanViewLoan");
    }
}
