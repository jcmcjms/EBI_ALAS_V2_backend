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
    /// <summary>Content types browsers render natively. Anything else stays
    /// `attachment` regardless of the request — XSS guard against a binary
    /// row carrying script-ish content.</summary>
    private static readonly HashSet<string> InlineSafeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/jpg", "image/gif",
    };

    public static void MapChecklistDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Checklist Documents")
            .RequireAuthorization();

        // ── GET /api/loans/{id}/checklist-documents ───────────────────────
        // Fetches checklist documents from BPB_BINARY_SERVER via OPENQUERY.
        // Shows required documents for the loan product and their upload status.
        //
        // Real-time stamp: after fetching the docs, we also refresh
        // DocumentsCompleteAt so the monitoring table's "Docs" badge
        // stays current without waiting for the background sweep.
        // The OPENQUERY call is already paid — the stamp update is a
        // single UPDATE on the local DB (zero extra remote calls).
        group.MapGet("/{id:int}/checklist-documents", async (
            int id, ClaimsPrincipal user, AppDbContext db,
            IChecklistDocumentRepository checklistRepo,
            ITimeProvider time,
            CancellationToken ct) =>
        {
            // First, get the loan_no from the loan application
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

            // Query BPB_BINARY_SERVER for checklist documents
            var documents = await checklistRepo.GetChecklistDocumentsAsync(loan.LoanNo, ct);

            // ── Real-time stamp refresh ─────────────────────────────────
            // The OPENQUERY call is already paid — check completeness and
            // update the stamp if it changed. This keeps the monitoring
            // table's "Docs" badge accurate the moment someone views the
            // document list, without waiting for the background sweep.
            var allUploaded = documents.Count > 0
                && documents.All(d => d.UploadStatus == "Uploaded");
            var newStamp = allUploaded ? time.UtcNow : (DateTime?)null;

            if (newStamp != loan.DocumentsCompleteAt)
            {
                // Targeted UPDATE — no need to load the full tracked entity.
                await db.LoanApplications
                    .Where(l => l.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.DocumentsCompleteAt, newStamp), ct);
            }

            return Results.Ok(ApiResponse<List<LoanChecklistDocumentDto>>.SuccessResponse(documents));
        })
        .WithName("GetChecklistDocuments")
        .RequireAuthorization("CanViewLoan");

        // ── GET /api/loans/checklist-documents/{docId}/view ──────────────
        // Fetches the actual document content from BPB_BINARY_SERVER for viewing.
        group.MapGet("/checklist-documents/{docId:int}/view", async (
            int docId, string? disposition, ClaimsPrincipal user, AppDbContext db,
            IChecklistDocumentRepository checklistRepo, CancellationToken ct) =>
        {
            if (!user.HasPermission(Permissions.LoansView))
                return Results.Json(
                    ApiResponse.ErrorResponse("You do not have permission to view documents."),
                    statusCode: StatusCodes.Status403Forbidden);

            var document = await checklistRepo.GetDocumentContentAsync(docId, ct);

            if (document is null || document.Content.Length == 0)
                return Results.NotFound(ApiResponse.ErrorResponse("Document not found or empty."));

            // `disposition=inline` + renderable type  → no fileDownloadName → inline.
            // Everything else keeps the filename → attachment (current behavior).
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
