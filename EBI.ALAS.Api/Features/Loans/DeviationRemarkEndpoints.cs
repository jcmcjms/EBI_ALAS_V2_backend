using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class DeviationRemarkEndpoints
{
    private static readonly string[] TerminalStatuses =
        ["Approved", "Rejected", "Disbursed", "OnGoing"];

    public static void MapDeviationRemarkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loan Deviation Remarks")
            .RequireAuthorization();

        // ── GET /api/loans/{id}/deviation-remarks ───────────────────────
        group.MapGet("/{id:int}/deviation-remarks", async (
            int id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.AsNoTracking()
                .Include(l => l.CreatedBy)
                .FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse("You do not have permission to view this loan's remarks."),
                    statusCode: StatusCodes.Status403Forbidden);

            var remarks = await db.DeviationRemarks.AsNoTracking()
                .Include(r => r.Author)
                .Where(r => r.LoanApplicationId == id)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .ToListAsync(ct);

            var encoderName = $"{loan.CreatedBy.FirstName} {loan.CreatedBy.LastName}";
            DeviationRemarkMessage Msg(DeviationRemark r) => new(
                r.Id, r.ParentRemarkId, $"{r.Author.FirstName} {r.Author.LastName}",
                r.AuthorRole, r.Body, r.CreatedAt, "remark");

            var threads = new List<DeviationThreadResponse>();

            foreach (var key in loan.DeviationDetails)
            {
                threads.Add(new DeviationThreadResponse(
                    key, key,
                    new DeviationRemarkMessage(0, null, encoderName, Roles.Encoder,
                        loan.DeviationJustifications.GetValueOrDefault(key, string.Empty),
                        loan.ApplicationDate, "submission"),
                    remarks.Where(r => r.DeviationKey == key).Select(Msg).ToList()));
            }

            if (!string.IsNullOrWhiteSpace(loan.FeeDeviationJustification))
            {
                threads.Add(new DeviationThreadResponse(
                    DeviationRemarkKeys.FeeOverride, "Fee override (notarial / doc stamps / insurance)",
                    new DeviationRemarkMessage(0, null, encoderName, Roles.Encoder,
                        loan.FeeDeviationJustification, loan.ApplicationDate, "submission"),
                    remarks.Where(r => r.DeviationKey == DeviationRemarkKeys.FeeOverride)
                        .Select(Msg).ToList()));
            }

            return Results.Ok(ApiResponse<List<DeviationThreadResponse>>.SuccessResponse(threads));
        })
        .WithName("GetDeviationRemarks")
        .RequireAuthorization("CanViewLoan");

        // ── POST /api/loans/{id}/deviation-remarks ──────────────────────
        group.MapPost("/{id:int}/deviation-remarks", async (
            int id, DeviationRemarkRequest request,
            IValidator<DeviationRemarkRequest> validator,
            ClaimsPrincipal user, AppDbContext db, IAuditLogger auditLogger,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed",
                    validation.Errors.Select(e => e.ErrorMessage).ToList()));

            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.CreatedById, l.Status, l.DeviationDetails,
                                   l.FeeDeviationJustification })
                .FirstOrDefaultAsync(ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse("You do not have permission to view this loan's remarks."),
                    statusCode: StatusCodes.Status403Forbidden);
            if (TerminalStatuses.Contains(loan.Status))
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Remarks are closed once the application is {loan.Status}."));

            var isFeeKey = request.DeviationKey == DeviationRemarkKeys.FeeOverride;
            var keyValid = isFeeKey
                ? !string.IsNullOrWhiteSpace(loan.FeeDeviationJustification)
                : loan.DeviationDetails.Contains(request.DeviationKey);
            if (!keyValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Unknown deviation key for this loan."));

            var userId = user.GetUserId();
            var role = user.GetRole();
            var isCreator = loan.CreatedById == userId;
            var mayWrite = role is Roles.Recommender or Roles.Evaluator
                        || role == Roles.Admin
                        || (isCreator && role == Roles.Encoder);
            if (!mayWrite)
                return Results.Json(ApiResponse.ErrorResponse(
                    "Your role cannot add remarks to deviations on this loan."),
                    statusCode: StatusCodes.Status403Forbidden);

            if (request.ParentRemarkId is { } parentId)
            {
                var parentOk = await db.DeviationRemarks.AnyAsync(r =>
                    r.Id == parentId && r.LoanApplicationId == id &&
                    r.DeviationKey == request.DeviationKey, ct);
                if (!parentOk)
                    return Results.BadRequest(ApiResponse.ErrorResponse(
                        "The remark you are replying to no longer belongs to this deviation."));
            }

            var remark = new DeviationRemark
            {
                LoanApplicationId = id,
                DeviationKey = request.DeviationKey,
                ParentRemarkId = request.ParentRemarkId,
                AuthorId = userId,
                AuthorRole = role,
                Body = request.Body.Trim(),
            };
            db.DeviationRemarks.Add(remark);
            await db.SaveChangesAsync(ct);

            await auditLogger.LogActionAsync(id, userId, "DeviationRemarkAdded",
                null, null, $"Remark on deviation '{request.DeviationKey}'");

            return Results.Created($"/api/loans/{id}/deviation-remarks",
                ApiResponse<DeviationRemarkMessage>.SuccessResponse(new(
                    remark.Id, remark.ParentRemarkId,
                    user.GetFirstName() + " " + user.GetLastName(),
                    role, remark.Body, remark.CreatedAt, "remark"), "Remark added."));
        })
        .WithName("AddDeviationRemark")
        .RequireAuthorization("CanViewLoan");
    }

    private static bool CanRead(ClaimsPrincipal user, int createdById) =>
        user.HasPermission(Permissions.LoansView) || user.GetUserId() == createdById;
}

public sealed record DeviationRemarkRequest
{
    public string DeviationKey { get; init; } = string.Empty;
    public int? ParentRemarkId { get; init; }
    public string Body { get; init; } = string.Empty;
}

public sealed class DeviationRemarkRequestValidator : AbstractValidator<DeviationRemarkRequest>
{
    public DeviationRemarkRequestValidator()
    {
        RuleFor(x => x.DeviationKey).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Body).NotEmpty().Length(3, 2000)
            .WithMessage("Remark must be between 3 and 2000 characters.");
    }
}

public sealed record DeviationRemarkMessage(
    int Id,
    int? ParentRemarkId,
    string AuthorName,
    string AuthorRole,
    string Body,
    DateTime CreatedAt,
    string Source);   // "submission" = encoder root, "remark" = conversation reply

public sealed record DeviationThreadResponse(
    string DeviationKey,
    string Title,
    DeviationRemarkMessage Root,
    IReadOnlyList<DeviationRemarkMessage> Replies);
