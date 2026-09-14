using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class ChecklistDocumentEndpoints
{
    public static void MapChecklistDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Checklist Documents")
            .RequireAuthorization();

        // ── GET /api/loans/{id}/checklist-documents ───────────────────────
        // Fetches checklist documents from BPB_BINARY_SERVER via OPENQUERY.
        // Shows required documents for the loan product and their upload status.
        group.MapGet("/{id:int}/checklist-documents", async (
            int id, ClaimsPrincipal user, AppDbContext db,
            IChecklistDocumentRepository checklistRepo, CancellationToken ct) =>
        {
            // First, get the loan_no from the loan application
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.LoanNo, l.CreatedById })
                .FirstOrDefaultAsync(ct);

            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            if (!CanRead(user, loan.CreatedById))
                return Results.Json(
                    ApiResponse.ErrorResponse("You do not have permission to view this loan's documents."),
                    statusCode: StatusCodes.Status403Forbidden);

            // Query BPB_BINARY_SERVER for checklist documents
            var documents = await checklistRepo.GetChecklistDocumentsAsync(loan.LoanNo, ct);

            return Results.Ok(ApiResponse<List<LoanChecklistDocumentDto>>.SuccessResponse(documents));
        })
        .WithName("GetChecklistDocuments")
        .RequireAuthorization("CanViewLoan");

        // ── GET /api/loans/checklist-documents/{docId}/view ──────────────
        // Fetches the actual document content from BPB_BINARY_SERVER for viewing.
        group.MapGet("/checklist-documents/{docId:int}/view", async (
            int docId, ClaimsPrincipal user, AppDbContext db,
            IChecklistDocumentRepository checklistRepo, CancellationToken ct) =>
        {
            // Verify user has permission to view loans
            if (!user.HasPermission(Permissions.LoansView))
                return Results.Json(
                    ApiResponse.ErrorResponse("You do not have permission to view documents."),
                    statusCode: StatusCodes.Status403Forbidden);

            var document = await checklistRepo.GetDocumentContentAsync(docId, ct);

            if (document is null || document.Content.Length == 0)
                return Results.NotFound(ApiResponse.ErrorResponse("Document not found or empty."));

            return Results.File(document.Content, document.ContentType, document.FileName);
        })
        .WithName("ViewChecklistDocument")
        .RequireAuthorization("CanViewLoan");
    }

    private static bool CanRead(ClaimsPrincipal user, int createdById) =>
        user.HasPermission(Permissions.LoansView) || user.GetUserId() == createdById;
}
