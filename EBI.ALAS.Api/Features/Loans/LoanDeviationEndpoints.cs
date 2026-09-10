using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class LoanDeviationEndpoints
{
    private static readonly string[] TerminalStatuses =
        ["Approved", "Rejected", "Disbursed", "OnGoing"];

    public static void MapLoanDeviationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loan Deviations")
            .RequireAuthorization();

        // ── GET /api/loans/{id}/deviations ──────────────────────────────
        // Deviations + their remark threads. TWO queries, grouped in memory:
        // one round-trip for the deviation rows, one for all remarks of the
        // loan — never one-per-thread (N+1).
        group.MapGet("/{id:int}/deviations", async (
            int id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id).Select(l => new { l.Id, l.CreatedById })
                .FirstOrDefaultAsync(ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse(
                    "You do not have permission to view this loan's deviations."),
                    statusCode: StatusCodes.Status403Forbidden);

            var deviations = await db.LoanDeviations.AsNoTracking()
                .Where(d => d.LoanApplicationId == id)
                .OrderBy(d => d.SortOrder).ThenBy(d => d.Id)
                .Select(d => new
                {
                    d.Id, d.ReasonText, d.EncoderJustification, d.IsFeeOverride, d.SortOrder,
                })
                .ToListAsync(ct);

            var remarks = await db.DeviationRemarks.AsNoTracking()
                .Where(r => r.Deviation.LoanApplicationId == id)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .Select(r => new DeviationRemarkResponse(
                    r.Id, r.LoanDeviationId, r.ParentRemarkId,
                    $"{r.Author.FirstName} {r.Author.LastName}",
                    r.AuthorRole, r.Body, r.CreatedAt))
                .ToListAsync(ct);

            var byDeviation = remarks.GroupBy(r => r.LoanDeviationId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var response = deviations.Select(d => new LoanDeviationResponse(
                d.Id, d.ReasonText, d.EncoderJustification, d.IsFeeOverride, d.SortOrder,
                byDeviation.TryGetValue(d.Id, out var rs) ? rs : [])).ToList();

            return Results.Ok(ApiResponse<List<LoanDeviationResponse>>.SuccessResponse(response));
        })
        .WithName("GetLoanDeviations")
        .RequireAuthorization("CanViewLoan");

        // ── POST /api/loans/{id}/deviations/{deviationId}/remarks ───────
        // Four-eyes conversation on ONE specific deviation:
        //   Recommender / Evaluator remark on any deviation of any loan they can view.
        //   The encoder (creator) answers remarks on their own application.
        //   Approver reads only; Admin may participate.
        //   Closed once the loan reaches a terminal status.
        group.MapPost("/{id:int}/deviations/{deviationId:int}/remarks", async (
            int id, int deviationId, AddDeviationRemarkRequest request,
            IValidator<AddDeviationRemarkRequest> validator,
            ClaimsPrincipal user, AppDbContext db, IAuditLogger auditLogger,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed",
                    validation.Errors.Select(e => e.ErrorMessage).ToList()));

            var deviation = await db.LoanDeviations
                .Include(d => d.LoanApplication)
                .FirstOrDefaultAsync(d => d.Id == deviationId && d.LoanApplicationId == id, ct);
            if (deviation is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Deviation not found on this loan."));

            var loan = deviation.LoanApplication;
            if (!CanRead(user, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse(
                    "You do not have permission to view this loan's deviations."),
                    statusCode: StatusCodes.Status403Forbidden);
            if (TerminalStatuses.Contains(loan.Status))
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Remarks are closed once the application is {loan.Status}."));

            var userId = user.GetUserId();
            var role = user.GetRole();
            var isCreator = loan.CreatedById == userId;
            var mayWrite = role is Roles.Recommender or Roles.Evaluator or Roles.Admin
                        || (isCreator && role == Roles.Encoder);
            if (!mayWrite)
                return Results.Json(ApiResponse.ErrorResponse(
                    "Your role cannot add remarks to this deviation."),
                    statusCode: StatusCodes.Status403Forbidden);

            if (request.ParentRemarkId is { } parentId)
            {
                var parentOk = await db.DeviationRemarks.AnyAsync(r =>
                    r.Id == parentId && r.LoanDeviationId == deviationId, ct);
                if (!parentOk)
                    return Results.BadRequest(ApiResponse.ErrorResponse(
                        "The remark you are replying to no longer belongs to this deviation."));
            }

            var remark = new DeviationRemark
            {
                LoanDeviationId = deviationId,
                ParentRemarkId = request.ParentRemarkId,
                AuthorId = userId,
                AuthorRole = role,
                Body = request.Body.Trim(),
            };
            db.DeviationRemarks.Add(remark);
            await db.SaveChangesAsync(ct);

            await auditLogger.LogActionAsync(id, userId, "DeviationRemarkAdded",
                null, null, $"Remark on deviation '{deviation.ReasonText}'");

            return Results.Created($"/api/loans/{id}/deviations",
                ApiResponse<DeviationRemarkResponse>.SuccessResponse(new(
                    remark.Id, remark.LoanDeviationId, remark.ParentRemarkId,
                    user.GetFirstName() + " " + user.GetLastName(),
                    role, remark.Body, remark.CreatedAt), "Remark added."));
        })
        .WithName("AddDeviationRemark")
        .RequireAuthorization("CanViewLoan");
    }

    private static bool CanRead(ClaimsPrincipal user, int createdById) =>
        user.HasPermission(Permissions.LoansView) || user.GetUserId() == createdById;
}

public sealed record AddDeviationRemarkRequest
{
    public string Body { get; init; } = string.Empty;
    public int? ParentRemarkId { get; init; }
}

public sealed class AddDeviationRemarkRequestValidator : AbstractValidator<AddDeviationRemarkRequest>
{
    public AddDeviationRemarkRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().Length(3, 2000)
            .WithMessage("Remark must be between 3 and 2000 characters.");
    }
}

public sealed record DeviationRemarkResponse(
    int Id,
    int LoanDeviationId,
    int? ParentRemarkId,
    string AuthorName,
    string AuthorRole,
    string Body,
    DateTime CreatedAt);

public sealed record LoanDeviationResponse(
    int Id,
    string ReasonText,
    string EncoderJustification,
    bool IsFeeOverride,
    int SortOrder,
    IReadOnlyList<DeviationRemarkResponse> Remarks);
