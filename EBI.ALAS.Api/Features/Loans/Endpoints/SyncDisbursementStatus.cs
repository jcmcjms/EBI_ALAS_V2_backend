using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Shared.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.WebLoans;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
namespace EBI.ALAS.Api.Features.Loans.Endpoints;
public static class SyncDisbursementStatus
{
    public static void MapSyncDisbursementStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();
        group.MapPost("/sync-disbursement-status", async (
            string loanNo,
            ILoanRepository loanRepository,
            IWebLoanRepository webLoanRepository,
            ILoanWorkflowService workflowService,
            IAuditLogger auditLogger,
            IRealtimeNotificationService realtimeService,
            AppDbContext db,
            ITimeProvider timeProvider,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(loanNo))
                return Results.BadRequest(ApiResponse.ErrorResponse("loanNo is required."));
            var loan = await loanRepository.GetByLoanNoAsync(loanNo.Trim(), ct);
            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse($"Loan with PN '{loanNo}' not found."));
            var preLoan = await webLoanRepository.GetPreLoanDataByLoanNoAsync(loanNo.Trim(), ct);
            if (preLoan is null)
                return Results.Ok(ApiResponse<object>.SuccessResponse(new
                {
                    loanId = loan.Id,
                    lamId = loan.LamId,
                    currentStatus = loan.LoanNo,
                    currentLoanStatus = loan.Status,
                    synced = false,
                    reason = "No matching pre_loan_data row in webloan.",
                }));
            string? targetStatus = null;
            string? syncReason = null;
            if (preLoan.ReleasedDate is not null && !string.IsNullOrWhiteSpace(preLoan.ReleasedBy))
            {
                if (loan.Status is "Disbursed" or "OnGoing")
                {
                    return Results.Ok(ApiResponse<object>.SuccessResponse(new
                    {
                        loanId = loan.Id,
                        lamId = loan.LamId,
                        loanNo = loan.LoanNo,
                        currentStatus = loan.Status,
                        synced = false,
                        reason = "Already at or past Disbursed.",
                    }));
                }
                if (loan.Status == "ForDisbursement")
                {
                    targetStatus = "Disbursed";
                    syncReason = $"Released on {preLoan.ReleasedDate:yyyy-MM-dd} by {preLoan.ReleasedBy}.";
                }
                else if (loan.Status == "Approved")
                {
                    targetStatus = "ForDisbursement";
                    syncReason = $"Approved on {preLoan.ApprovedDate:yyyy-MM-dd} by {preLoan.ApprovedBy}.";
                }
            }
            else if (preLoan.ApprovedDate is not null && !string.IsNullOrWhiteSpace(preLoan.ApprovedBy))
            {
                if (loan.Status is "ForDisbursement" or "Disbursed" or "OnGoing")
                {
                    return Results.Ok(ApiResponse<object>.SuccessResponse(new
                    {
                        loanId = loan.Id,
                        lamId = loan.LamId,
                        loanNo = loan.LoanNo,
                        currentStatus = loan.Status,
                        synced = false,
                        reason = "Already at or past ForDisbursement.",
                    }));
                }
                if (loan.Status == "Approved")
                {
                    targetStatus = "ForDisbursement";
                    syncReason = $"Approved on {preLoan.ApprovedDate:yyyy-MM-dd} by {preLoan.ApprovedBy}.";
                }
            }
            if (targetStatus is null)
            {
                return Results.Ok(ApiResponse<object>.SuccessResponse(new
                {
                    loanId = loan.Id,
                    lamId = loan.LamId,
                    loanNo = loan.LoanNo,
                    currentStatus = loan.Status,
                    synced = false,
                    reason = $"No qualifying webloan state for current ALAS status '{loan.Status}'.",
                }));
            }
            if (!workflowService.IsValidTransition(loan.Status, targetStatus, Roles.Admin))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Invalid transition from '{loan.Status}' to '{targetStatus}'."));
            }
            var systemUserId = await ResolveSystemUserIdAsync(db, cache, ct);
            var fromStatus = loan.Status;
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                loan.Status = targetStatus;
                loan.LastActionDate = timeProvider.UtcNow;
                await loanRepository.UpdateAsync(loan);
                await auditLogger.LogActionAsync(
                    loan.Id, systemUserId, "StatusChanged", fromStatus, targetStatus, syncReason);
                await tx.CommitAsync(ct);
            });
            await realtimeService.NotifyDashboardUpdateAsync(loan.BranchCode);
            return Results.Ok(ApiResponse<object>.SuccessResponse(new
            {
                loanId = loan.Id,
                lamId = loan.LamId,
                loanNo = loan.LoanNo,
                previousStatus = fromStatus,
                currentStatus = targetStatus,
                synced = true,
                reason = syncReason,
            }));
        })
        .WithName("SyncDisbursementStatus")
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");
    }
    private static async Task<int> ResolveSystemUserIdAsync(
        AppDbContext db, IMemoryCache cache, CancellationToken ct)
    {
        const string cacheKey = "system:userId";
        if (cache.TryGetValue<int>(cacheKey, out var id))
            return id;
        id = await db.Users.AsNoTracking()
            .Where(u => u.Username == "system")
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        if (id == 0)
            throw new InvalidOperationException(
                "System user not found. Ensure DbInitializer has seeded a user with Username == 'system'.");
        cache.Set(cacheKey, id, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            Size = 1,
        });
        return id;
    }
}
