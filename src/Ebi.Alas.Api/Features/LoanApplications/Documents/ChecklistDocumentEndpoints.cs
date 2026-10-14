using System.Security.Claims;
using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.Documents;

public sealed record LoanChecklistDocumentDto(
    string LoanNo,
    string LoanProduct,
    string IdCode,
    string? ChecklistDescription,
    int? DocId,
    string? DocStr,
    string? MiniStr,
    string? ContentType,
    DateTimeOffset? Created,
    string? UploadedBy,
    string UploadStatus);

public sealed record DocumentRemarkDto(
    Guid Id,
    string ChecklistIdCode,
    Guid? DocId,
    string Body,
    string AuthorName,
    DateTimeOffset CreatedAt);

public static class ChecklistDocumentEndpoints
{
    private static readonly HashSet<string> InlineSafeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/jpg", "image/gif",
    };

    public static void MapChecklistDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Documents").RequireAuthorization();

        group.MapGet("/{id:guid}/checklist-documents", async (
            Guid id,
            AlasDbContext db,
            IChecklistDocumentReader checklistReader,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.ProductCode, l.BranchId, l.FormJson })
                .FirstOrDefaultAsync(ct);
            if (loan is null)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            if (!caller.CanAccessAllBranches && loan.BranchId != caller.BranchId)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            var requirements = await db.LoanProductDocumentRequirements.AsNoTracking()
                .Where(r => r.ProductCode == loan.ProductCode)
                .OrderBy(r => r.Code)
                .Select(r => r.Code)
                .ToListAsync(ct);

            var loanNo = ChecklistDocumentReader.ExtractLoanNo(loan.FormJson);
            if (loanNo is null)
            {
                return Results.Ok(Array.Empty<LoanChecklistDocumentDto>());
            }

            var documents = await checklistReader.GetChecklistDocumentsAsync(loanNo, requirements, ct);
            return Results.Ok(documents);
        })
        .WithName("GetChecklistDocuments");

        group.MapGet("/checklist-documents/{docId:int}/view", async (
            int docId,
            string? disposition,
            IChecklistDocumentReader checklistReader,
            CancellationToken ct) =>
        {
            var document = await checklistReader.GetDocumentContentAsync(docId, ct);
            if (document?.MiniStr is null || document.MiniStr.Length == 0)
            {
                throw new Composition.Errors.NotFoundException("Document", docId.ToString());
            }

            byte[] content;
            try
            {
                content = Convert.FromBase64String(document.MiniStr);
            }
            catch (FormatException)
            {
                content = [];
            }

            if (content.Length == 0)
            {
                throw new Composition.Errors.NotFoundException("Document", docId.ToString());
            }

            var contentType = string.IsNullOrWhiteSpace(document.ContentType)
                ? "application/octet-stream"
                : document.ContentType;
            var wantsInline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase)
                && InlineSafeTypes.Contains(contentType);
            var fileName = $"document-{docId}{FileExtension(contentType)}";
            return wantsInline
                ? Results.File(content, contentType)
                : Results.File(content, contentType, fileName);
        })
        .WithName("ViewChecklistDocument");

        group.MapGet("/{id:guid}/document-remarks", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var exists = await db.LoanApplications.AsNoTracking()
                .AnyAsync(l => l.Id == id, ct);
            if (!exists)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            _ = caller;
            return Results.Ok(Array.Empty<DocumentRemarkDto>());
        })
        .WithName("GetDocumentRemarks");
    }

    private static string FileExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tiff",
        "application/msword" => ".doc",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.ms-excel" => ".xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "text/plain" => ".txt",
        "text/html" => ".html",
        _ => ".bin",
    };
}
