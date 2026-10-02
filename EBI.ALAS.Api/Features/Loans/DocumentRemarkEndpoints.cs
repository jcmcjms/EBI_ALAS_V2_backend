using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Shared.Models;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.Loans;
public static class DocumentRemarkEndpoints
{
    private static readonly string[] TerminalStatuses =
        ["Approved", "Rejected", "Disbursed", "OnGoing", "Cancelled"];
    private static readonly string[] WriterRoles =
        [Roles.Recommender, Roles.Evaluator, Roles.Approver, Roles.Admin];
    public static void MapDocumentRemarkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loan Document Remarks")
            .RequireAuthorization();
        group.MapGet("/{id:int}/document-remarks", async (
            int id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.BranchCode, l.CreatedById })
                .FirstOrDefaultAsync(ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.BranchCode, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse(
                    "You do not have permission to view this loan's document remarks."),
                    statusCode: StatusCodes.Status403Forbidden);
            var remarks = await db.DocumentRemarks.AsNoTracking()
                .Where(r => r.LoanApplicationId == id)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .Select(r => new DocumentRemarkResponse(
                    r.Id, r.ChecklistIdCode, r.DocId, r.ParentRemarkId,
                    $"{r.Author.FirstName} {r.Author.LastName}",
                    r.AuthorRole, r.Body, r.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(ApiResponse<List<DocumentRemarkResponse>>.SuccessResponse(remarks));
        })
        .WithName("GetDocumentRemarks")
        .RequireAuthorization("CanViewLoan");
        group.MapPost("/{id:int}/document-remarks", async (
            int id, AddDocumentRemarkRequest request,
            IValidator<AddDocumentRemarkRequest> validator,
            ClaimsPrincipal user, AppDbContext db,
            IChecklistDocumentRepository checklistRepo,
            IAuditLogger auditLogger,
            IRemarkNotificationService remarkNotifier, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed",
                    validation.Errors.Select(e => e.ErrorMessage).ToList()));
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.LoanNo, l.LamId, l.BranchCode, l.CreatedById, l.Status })
                .FirstOrDefaultAsync(ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.BranchCode, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse(
                    "You do not have permission to view this loan's document remarks."),
                    statusCode: StatusCodes.Status403Forbidden);
            var userId = user.GetUserId();
            var role = user.GetRole();
            var isReviewer = WriterRoles.Contains(role);
            var isOwningEncoder = role == Roles.Encoder && loan.CreatedById == userId;
            if (!isReviewer && !isOwningEncoder)
                return Results.Json(ApiResponse.ErrorResponse(
                    "Only the Recommender, Evaluator, Approver, or the submitting Encoder may add document remarks."),
                    statusCode: StatusCodes.Status403Forbidden);
            if (TerminalStatuses.Contains(loan.Status))
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Remarks are closed once the application is {loan.Status}."));
            var checklist = await checklistRepo.GetChecklistDocumentsAsync(loan.LoanNo, ct);
            var item = checklist.FirstOrDefault(c => c.IdCode == request.ChecklistIdCode);
            if (item is null)
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Unknown checklist document for this loan."));
            var parentId = request.ParentRemarkId;
            if (parentId.HasValue)
            {
                var parentOk = await db.DocumentRemarks.AnyAsync(r =>
                    r.Id == parentId.Value && r.LoanApplicationId == id
                    && r.ChecklistIdCode == request.ChecklistIdCode, ct);
                if (!parentOk)
                    return Results.BadRequest(ApiResponse.ErrorResponse(
                        "The remark you are replying to no longer belongs to this document."));
            }
            var remark = new DocumentRemark
            {
                LoanApplicationId = id,
                ChecklistIdCode = request.ChecklistIdCode,
                DocId = request.DocId ?? item.DocId,
                ParentRemarkId = parentId,
                AuthorId = userId,
                AuthorRole = role,
                Body = request.Body.Trim(),
            };
            db.DocumentRemarks.Add(remark);
            await db.SaveChangesAsync(ct);
            await auditLogger.LogActionAsync(id, userId, "DocumentRemarkAdded", null, null,
                $"Remark on document '{item.ChecklistDescription ?? request.ChecklistIdCode}'");
            var docLabel = item.ChecklistDescription ?? request.ChecklistIdCode;
            var actorName = $"{user.GetFirstName()} {user.GetLastName()}";
            var link = $"/loans/approval/{id}";
            if (parentId is { } replyToId)
            {
                var parentAuthorId = await db.DocumentRemarks.AsNoTracking()
                    .Where(r => r.Id == replyToId)
                    .Select(r => r.AuthorId)
                    .FirstOrDefaultAsync(ct);
                await remarkNotifier.NotifyAsync(new RemarkNotifyRequest(
                    id, userId,
                    "New reply to your remark",
                    $"{actorName} replied to your remark on '{docLabel}' ({loan.LamId}).",
                    link,
                    parentAuthorId == default ? null : parentAuthorId), ct);
            }
            else
            {
                await remarkNotifier.NotifyAsync(new RemarkNotifyRequest(
                    id, userId,
                    "New document remark",
                    $"{actorName} remarked on '{docLabel}' of your application {loan.LamId}.",
                    link,
                    loan.CreatedById), ct);
            }
            return Results.Created($"/api/loans/{id}/document-remarks",
                ApiResponse<DocumentRemarkResponse>.SuccessResponse(new(
                    remark.Id, remark.ChecklistIdCode, remark.DocId, remark.ParentRemarkId,
                    actorName, role,
                    remark.Body, remark.CreatedAt), "Remark added."));
        })
        .WithName("AddDocumentRemark")
        .RequireAuthorization("CanViewLoan");
    }
    private static bool CanRead(ClaimsPrincipal user, string branchCode, int createdById)
    {
        if (user.GetUserId() == createdById) return true;
        if (user.GetRole() == Roles.Admin) return true;
        return user.HasPermission(Permissions.LoansView)
            && string.Equals(user.GetBranchCode(), branchCode, StringComparison.Ordinal);
    }
}
public sealed record AddDocumentRemarkRequest
{
    public string ChecklistIdCode { get; init; } = string.Empty;
    public int? DocId { get; init; }
    public int? ParentRemarkId { get; init; }
    public string Body { get; init; } = string.Empty;
}
public sealed class AddDocumentRemarkRequestValidator : AbstractValidator<AddDocumentRemarkRequest>
{
    public AddDocumentRemarkRequestValidator()
    {
        RuleFor(x => x.ChecklistIdCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Body).NotEmpty().Length(3, 2000)
            .WithMessage("Remark must be between 3 and 2000 characters.");
    }
}
public sealed record DocumentRemarkResponse(
    int Id,
    string ChecklistIdCode,
    int? DocId,
    int? ParentRemarkId,
    string AuthorName,
    string AuthorRole,
    string Body,
    DateTime CreatedAt);
