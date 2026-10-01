using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.Loans;
public static class ChecklistDocumentEndpoints
{
    private static readonly HashSet<string> InlineSafeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/jpg", "image/gif",
    };
    public static void MapChecklistDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Checklist Documents")
            .RequireAuthorization();
        group.MapGet("/{id:int}/checklist-documents", async (
            int id, ClaimsPrincipal user, AppDbContext db,
            IChecklistDocumentRepository checklistRepo,
            ITimeProvider time,
            CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.LoanNo, l.CreatedById, l.DocumentsCompleteAt })
                .FirstOrDefaultAsync(ct);
            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.CreatedById))
                return Results.Json(
                    ApiResponse.ErrorResponse("You do not have permission to view this loan's documents."),
                    statusCode: StatusCodes.Status403Forbidden);
            var documents = await checklistRepo.GetChecklistDocumentsAsync(loan.LoanNo, ct);
            var allUploaded = documents.Count > 0
                && documents.All(d => d.UploadStatus == "Uploaded");
            var newStamp = allUploaded ? time.UtcNow : (DateTime?)null;
            if (newStamp != loan.DocumentsCompleteAt)
            {
                await db.LoanApplications
                    .Where(l => l.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.DocumentsCompleteAt, newStamp), ct);
            }
            return Results.Ok(ApiResponse<List<LoanChecklistDocumentDto>>.SuccessResponse(documents));
        })
        .WithName("GetChecklistDocuments")
        .RequireAuthorization("CanViewLoan");
        group.MapGet("/checklist-documents/{docId:int}/view", async (
            int docId, string? disposition, ClaimsPrincipal user, AppDbContext db,
            IChecklistDocumentRepository checklistRepo, CancellationToken ct) =>
        {
            if (!user.HasPermission(Permissions.LoansView))
                return Results.Json(
                    ApiResponse.ErrorResponse("You do not have permission to view documents."),
                    statusCode: StatusCodes.Status403Forbidden);
            var loanNo = await checklistRepo.GetDocumentLoanNoAsync(docId, ct);
            if (string.IsNullOrEmpty(loanNo))
                return Results.NotFound(ApiResponse.ErrorResponse("Document not found or empty."));
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.LoanNo == loanNo)
                .Select(l => new { l.Id, l.CreatedById, l.BranchCode })
                .FirstOrDefaultAsync(ct);
            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Document not found or empty."));
            if (!CanRead(user, loan.CreatedById))
                return Results.NotFound(ApiResponse.ErrorResponse("Document not found or empty."));
            var document = await checklistRepo.GetDocumentContentAsync(docId, ct);
            if (document is null || document.Content.Length == 0)
                return Results.NotFound(ApiResponse.ErrorResponse("Document not found or empty."));
            var wantsInline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase);
            var inlineSafe = wantsInline
                && InlineSafeTypes.Contains(document.ContentType);
            return inlineSafe
                ? Results.File(document.Content, document.ContentType)
                : Results.File(document.Content, document.ContentType, document.FileName);
        })
        .WithName("ViewChecklistDocument")
        .RequireAuthorization("CanViewLoan");
    }
    private static bool CanRead(ClaimsPrincipal user, int createdById) =>
        user.HasPermission(Permissions.LoansView) || user.GetUserId() == createdById;
}
