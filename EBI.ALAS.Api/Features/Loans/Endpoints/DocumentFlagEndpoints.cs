using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

/// <summary>
/// Document flag endpoints — a deficiency is a FACT about paperwork, NOT a
/// routing decision. These endpoints record/clear the flag without touching
/// Status or workflow queues.
///
/// POST   /api/loans/{id}/document-flag  — flag missing documents
/// DELETE /api/loans/{id}/document-flag  — clear the flag
/// </summary>
public static class DocumentFlagEndpoints
{
    public static void MapDocumentFlagEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans/{id:int}/document-flag")
            .WithTags("Loans")
            .RequireAuthorization();

        // POST — flag missing documents
        group.MapPost("/", async (
            int id,
            [FromBody] FlagDocumentsRequest request,
            IValidator<FlagDocumentsRequest> validator,
            AppDbContext db,
            IDocumentGateService flagService,
            IRealtimeNotificationService realtime,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Validation failed",
                    errors.SelectMany(e => e.Value).ToList()));
            }

            var loan = await db.LoanApplications.FindAsync([id], ct);
            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            // Desk-role guard: the reviewer must be at the desk that matches
            // the loan's current status. Admin bypasses.
            var userRole = user.GetRole();
            var userId = user.GetUserId();
            if (userRole != Roles.Admin)
            {
                var requiredRole = DeskRoleFor(loan.Status);
                if (requiredRole is null || requiredRole != userRole)
                    return Results.Json(ApiResponse.ErrorResponse(
                        $"Only a {requiredRole ?? "reviewer"} can flag documents at the {loan.Status} desk."),
                        statusCode: StatusCodes.Status403Forbidden);
            }

            // Branch scoping: non-Admin users can only flag loans in their own branch.
            if (userRole != Roles.Admin)
            {
                var branchCode = user.GetBranchId();
                if (!string.Equals(loan.BranchCode, branchCode, StringComparison.Ordinal))
                    return Results.Forbid();
            }

            await flagService.FlagAsync(loan, request.MissingRequirementCodes, request.Reason, userId, ct);
            await realtime.NotifyDashboardUpdateAsync(loan.BranchCode);

            return Results.Ok(ApiResponse.SuccessResponse("Documents flagged. The encoder has been notified."));
        })
        .WithName("FlagDocuments")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(403)
        .Produces<ApiResponse>(404);

        // DELETE — clear the flag
        group.MapDelete("/", async (
            int id,
            AppDbContext db,
            IDocumentGateService flagService,
            IRealtimeNotificationService realtime,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.FindAsync([id], ct);
            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            if (loan.DocumentsFlaggedAt is null)
                return Results.BadRequest(ApiResponse.ErrorResponse("This loan has no active document flag."));

            // Only the flagger, an admin, or the encoder (after uploading) can clear.
            var userRole = user.GetRole();
            var userId = user.GetUserId();
            var isFlagger = loan.DocumentsFlaggedById == userId;
            var isEncoder = loan.CreatedById == userId;
            if (userRole != Roles.Admin && !isFlagger && !isEncoder)
                return Results.Json(ApiResponse.ErrorResponse(
                    "Only the flagger, the encoder, or an admin can clear the document flag."),
                    statusCode: StatusCodes.Status403Forbidden);

            var cleared = await flagService.ClearAsync(loan, userId, "Manual clear by user.", ct);
            if (!cleared)
                return Results.BadRequest(ApiResponse.ErrorResponse("No active flag to clear."));

            await realtime.NotifyDashboardUpdateAsync(loan.BranchCode);
            return Results.Ok(ApiResponse.SuccessResponse("Document flag cleared."));
        })
        .WithName("ClearDocumentFlag")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(403)
        .Produces<ApiResponse>(404);
    }

    /// <summary>
    /// Maps a loan status to the role that owns that desk.
    /// Returns null for statuses that aren't review desks.
    /// </summary>
    private static string? DeskRoleFor(string status) => status switch
    {
        "ForRecommendation" => Roles.Recommender,
        "ForChecking" => Roles.Evaluator,
        "ForApproval" => Roles.Approver,
        _ => null,
    };
}

/// <summary>
/// Request to flag documents as missing.
/// </summary>
public class FlagDocumentsRequest
{
    /// <summary>Checklist requirement codes the reviewer flags as missing.</summary>
    public IReadOnlyList<string> MissingRequirementCodes { get; init; } = [];

    /// <summary>Written reason for flagging (min 5 chars).</summary>
    public string Reason { get; init; } = string.Empty;
}

public class FlagDocumentsValidator : AbstractValidator<FlagDocumentsRequest>
{
    public FlagDocumentsValidator()
    {
        RuleFor(x => x.MissingRequirementCodes)
            .NotEmpty().WithMessage("At least one missing requirement code is required.")
            .ForEach(c => c.NotEmpty().MaximumLength(64));

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when flagging documents.")
            .MinimumLength(5).WithMessage("Reason must be at least 5 characters.")
            .MaximumLength(2000).WithMessage("Reason must not exceed 2000 characters.");
    }
}
